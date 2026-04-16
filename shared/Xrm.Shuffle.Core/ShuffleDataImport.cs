namespace Cinteros.Crm.Utils.Shuffle
{
    using Cinteros.Crm.Utils.Shuffle.Types;
    using global::Xrm.Utils.Core.Common.Extensions;
    using global::Xrm.Utils.Core.Common.Interfaces;
    using global::Xrm.Utils.Core.Common.Misc;
    using Microsoft.Crm.Sdk.Messages;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using Microsoft.Xrm.Sdk.Query;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.ServiceModel;

    public partial class Shuffler
    {
        #region Bulk Operation Support Cache

        /// <summary>
        /// Cache for CreateMultiple support per entity logical name.
        /// True = supported, False = not supported, null = not yet checked.
        /// </summary>
        private Dictionary<string, bool> createMultipleSupportCache = new Dictionary<string, bool>();

        /// <summary>
        /// Cache for UpdateMultiple support per entity logical name.
        /// True = supported, False = not supported, null = not yet checked.
        /// </summary>
        private Dictionary<string, bool> updateMultipleSupportCache = new Dictionary<string, bool>();

        /// <summary>
        /// Cache for UpsertMultiple support per entity logical name.
        /// True = supported, False = not supported, null = not yet checked.
        /// </summary>
        private Dictionary<string, bool> upsertMultipleSupportCache = new Dictionary<string, bool>();

        /// <summary>
        /// Cache for Upsert (single) support per entity logical name.
        /// True = supported, False = not supported, null = not yet checked.
        /// </summary>
        private Dictionary<string, bool> upsertSupportCache = new Dictionary<string, bool>();

        /// <summary>
        /// Fault code indicating the message is not implemented (used for on-premises fallback detection).
        /// </summary>
        private const int MessageNotImplementedErrorCode = unchecked((int)0x80040265);

        #endregion Bulk Operation Support Cache

        #region Private Methods

        private static bool EntityAttributesEqual(IExecutionContainer container, List<string> matchattributes, Entity entity1, Entity entity2)
        {
            var match = true;
            foreach (var attr in matchattributes)
            {
                var srcvalue = "";
                if (attr == container.Entity(entity1.LogicalName).PrimaryIdAttribute)
                {
                    srcvalue = entity1.Id.ToString();
                }
                else
                {
                    srcvalue = container.AttributeAsBaseType(entity1, attr, "<null>", true).ToString();
                }
                var trgvalue = container.AttributeAsBaseType(entity2, attr, "<null>", true).ToString();
                if (srcvalue != trgvalue)
                {
                    match = false;
                    break;
                }
            }
            return match;
        }

        private static string GetEntityDisplayString(IExecutionContainer container, DataBlockImportMatch match, Entity cdEntity)
        {
            var unique = new List<string>();
            if (match != null && match.Attribute.Length > 0)
            {
                foreach (var attribute in match.Attribute)
                {
                    var matchdisplay = attribute.Display;
                    if (string.IsNullOrEmpty(matchdisplay))
                    {
                        matchdisplay = attribute.Name;
                    }
                    var matchvalue = "<null>";
                    if (cdEntity.Contains(matchdisplay, true))
                    {
                        if (cdEntity[matchdisplay] is EntityReference)
                        {   // Don't use PropertyAsString, that would perform GetRelated that we don't want due to performance
                            var entref = cdEntity.GetAttribute<EntityReference>(matchdisplay, null);
                            if (!string.IsNullOrEmpty(entref.Name))
                            {
                                matchvalue = entref.Name;
                            }
                            else
                            {
                                matchvalue = entref.LogicalName + ":" + entref.Id.ToString();
                            }
                        }
                        else
                        {
                            matchvalue = cdEntity.FormattedValues.Contains(matchdisplay)
                                ? cdEntity.FormattedValues[matchdisplay]
                                : cdEntity[matchdisplay]?.ToString() ?? "";
                        }
                    }
                    unique.Add(matchvalue);
                }
            }
            if (unique.Count == 0)
            {
                unique.Add(cdEntity.Id.ToString());
            }
            return string.Join(", ", unique);
        }

        private static void ReplaceUpdateInfo(Entity cdEntity)
        {
            var removeAttr = new List<string>();
            var newAttr = new List<KeyValuePair<string, object>>();
            foreach (var attr in cdEntity.Attributes)
            {
                if (attr.Key == "createdby")
                {
                    if (!cdEntity.Attributes.Contains("createdonbehalfby"))
                    {
                        newAttr.Add(new KeyValuePair<string, object>("createdonbehalfby", attr.Value));
                    }
                    removeAttr.Add("createdby");
                }
                else if (attr.Key == "modifiedby")
                {
                    if (!cdEntity.Attributes.Contains("modifiedonbehalfby"))
                    {
                        newAttr.Add(new KeyValuePair<string, object>("modifiedonbehalfby", attr.Value));
                    }
                    removeAttr.Add("modifiedby");
                }
                else if (attr.Key == "createdon")
                {
                    if (!cdEntity.Attributes.Contains("overriddencreatedon"))
                    {
                        newAttr.Add(new KeyValuePair<string, object>("overriddencreatedon", attr.Value));
                    }
                    removeAttr.Add("createdon");
                }
            }
            foreach (var key in removeAttr)
            {
                cdEntity.Attributes.Remove(key);
            }
            if (newAttr.Count > 0)
            {
                cdEntity.Attributes.AddRange(newAttr);
            }
        }

        private EntityCollection GetAllRecordsForMatching(IExecutionContainer container, List<string> allattributes, Entity cdEntity)
        {
            container.StartSection(MethodBase.GetCurrentMethod().Name);
            var qMatch = new QueryExpression(cdEntity.LogicalName)
            {
                ColumnSet = new ColumnSet(allattributes.ToArray())
            };
#if DEBUG
            container.Log($"Retrieving all records for {cdEntity.LogicalName}:\n{container.ConvertToFetchXml(qMatch)}");
#endif
            var matches = container.RetrieveMultiple(qMatch);
            SendLine(container, $"Pre-retrieved {matches.Count()} records for matching");
            container.EndSection();
            return matches;
        }

        private List<string> GetMatchAttributes(DataBlockImportMatch match)
        {
            var result = new List<string>();
            if (match != null)
            {
                foreach (var attribute in match.Attribute)
                {
                    var matchattr = attribute.Name;
                    if (string.IsNullOrEmpty(matchattr))
                    {
                        throw new ArgumentOutOfRangeException("Match Attribute name not set");
                    }
                    result.Add(matchattr);
                }
            }
            return result;
        }

        private EntityCollection GetMatchingRecords(IExecutionContainer container, Entity cdEntity, List<string> matchattributes, List<string> updateattributes, bool preretrieveall, ref EntityCollection cAllRecordsToMatch)
        {
            container.StartSection(MethodBase.GetCurrentMethod().Name);
            EntityCollection matches = null;
            var allattributes = new List<string>
            {
                container.Entity(cdEntity.LogicalName).PrimaryIdAttribute
            };
            if (cdEntity.Contains("ownerid"))
            {
                allattributes.Add("ownerid");
            }
            if (cdEntity.Contains("statecode") || cdEntity.Contains("statuscode"))
            {
                allattributes.Add("statecode");
                allattributes.Add("statuscode");
            }
            allattributes = allattributes.Union(matchattributes.Union(updateattributes)).ToList();
            if (preretrieveall)
            {
                if (cAllRecordsToMatch == null)
                {
                    cAllRecordsToMatch = GetAllRecordsForMatching(container, allattributes, cdEntity);
                }
                matches = GetMatchingRecordsFromPreRetrieved(container, matchattributes, cdEntity, cAllRecordsToMatch);
            }
            else
            {
                var qMatch = new QueryExpression(cdEntity.LogicalName)
                {
                    // We need to be able to see if any attributes have changed, so lets make sure matching records have all the attributes that will be updated
                    ColumnSet = new ColumnSet(allattributes.ToArray())
                };

                foreach (var matchattr in matchattributes)
                {
                    object value = null;
                    if (cdEntity.Contains(matchattr))
                    {
                        value = container.AttributeAsBaseType(cdEntity, matchattr, null, false);
                    }
                    else if (matchattr == container.Entity(cdEntity.LogicalName).PrimaryIdAttribute)
                    {
                        value = cdEntity.Id;
                    }
                    if (value != null)
                    {
                        Query.AppendCondition(qMatch.Criteria, LogicalOperator.And, matchattr, Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, value);
                    }
                    else
                    {
                        Query.AppendCondition(qMatch.Criteria, LogicalOperator.And, matchattr, Microsoft.Xrm.Sdk.Query.ConditionOperator.Null, null);
                    }
                }
#if DEBUG
                container.Log($"Finding matches for {cdEntity.LogicalName}:\n{container.ConvertToFetchXml(qMatch)}");
#endif
                matches = container.RetrieveMultiple(qMatch);
            }
            container.EndSection();
            return matches;
        }

        private EntityCollection GetMatchingRecordsFromPreRetrieved(IExecutionContainer container, List<string> matchattributes, Entity cdEntity, EntityCollection cAllRecordsToMatch)
        {
            container.StartSection(MethodBase.GetCurrentMethod().Name);
            container.Log($"Searching matches for: {cdEntity.Id} {cdEntity.LogicalName}");
            var result = new EntityCollection();
            foreach (var cdRecord in cAllRecordsToMatch.Entities)
            {
                if (EntityAttributesEqual(container, matchattributes, cdEntity, cdRecord))
                {
                    result.Add(cdRecord);
                    container.Log($"Found match: {cdRecord.Id} {cdRecord.LogicalName}");
                }
            }
            container.Log($"Returned matches: {result.Count()}");
            container.EndSection();
            return result;
        }

        private List<string> GetUpdateAttributes(EntityCollection entities)
        {
            var result = new HashSet<string>();
            foreach (var entity in entities.Entities)
            {
                foreach (var attribute in entity.Attributes.Keys)
                {
                    result.Add(attribute);
                }
            }
            return result.ToList();
        }

        private Tuple<int, int, int, int, int, EntityReferenceCollection> ImportDataBlock(IExecutionContainer container, DataBlock block, EntityCollection cEntities)
        {
            container.StartSection("ImportDataBlock");
            var created = 0;
            var updated = 0;
            var skipped = 0;
            var deleted = 0;
            var failed = 0;
            var references = new EntityReferenceCollection();

            var name = block.Name;
            container.Log($"Block: {name}");
            SendStatus(name, null);
            SendLine(container);

            if (block.Import != null)
            {
                var includeid = block.Import.CreateWithId;
                var save = block.Import.Save;
                var delete = block.Import.Delete;
                var updateinactive = block.Import.UpdateInactive;
                var updateidentical = block.Import.UpdateIdentical;
                if (block.Import.OverwriteSpecified)
                {
                    SendLine(container, "DEPRECATED use of attribute Overwrite!");
                    save = block.Import.Overwrite ? SaveTypes.CreateUpdate : SaveTypes.CreateOnly;
                }
                var matchattributes = GetMatchAttributes(block.Import.Match);
                var updateattributes = !updateidentical ? GetUpdateAttributes(cEntities) : new List<string>();
                var preretrieveall = block.Import.Match?.PreRetrieveAll == true;
                var batchsize = Math.Max(1, Math.Min(block.Import.BatchSize, 1000));
                var deferStateAndOwner = block.Import.DeferStateAndOwner;

                if (deferStateAndOwner)
                {
                    SendLine(container, "DeferStateAndOwner enabled - state/owner will be applied in second pass");
                }

                // Determine if we can use Upsert path (eliminates need for PreRetrieveAll queries)
                // Upsert is optimal when: Save=CreateUpdate, records have ID (CreateWithId), records are batchable,
                // AND UpdateIdentical=true (because Upsert cannot skip identical records - we don't retrieve existing data to compare)
                var canUseUpsert = save == SaveTypes.CreateUpdate && 
                                   includeid && 
                                   matchattributes.Count > 0 &&
                                   delete == DeleteTypes.None &&
                                   updateidentical;

                if (canUseUpsert)
                {
                    SendLine(container, "Upsert path enabled - records will be upserted without pre-retrieval queries");
                    if (preretrieveall)
                    {
                        SendLine(container, "Note: PreRetrieveAll is not needed when using Upsert and will be skipped");
                    }
                }

                SendLine(container);
                SendLine(container, $"Importing block {name} - {cEntities.Count()} records ");

                var i = 1;

                if (delete == DeleteTypes.All && (matchattributes.Count == 0))
                {   // All records shall be deleted, no match attribute defined, so just get all and delete all
                    var entity = block.Entity;
                    var qDelete = new QueryExpression(entity);

                    qDelete.ColumnSet.AddColumn(container.Entity(entity).PrimaryNameAttribute);
                    var deleterecords = container.RetrieveMultiple(qDelete);
                    SendLine(container, $"Deleting ALL {entity} - {deleterecords.Count()} records");
                    var deleteBatch = new List<Entity>();
                    foreach (var record in deleterecords.Entities)
                    {
                        SendLine(container, "{0:000} Deleting existing: {1}", i, record);
                        deleteBatch.Add(record);
                        if (deleteBatch.Count >= batchsize)
                        {
                            FlushPendingDeletes(container, deleteBatch, ref deleted, ref failed);
                        }
                        i++;
                    }
                    FlushPendingDeletes(container, deleteBatch, ref deleted, ref failed);
                }
                var totalRecords = cEntities.Count();
                i = 1;
                EntityCollection cAllRecordsToMatch = null;
                var pendingCreates = new List<PendingCreate>();
                var pendingUpdates = new List<PendingUpdate>();
                var pendingUpserts = new List<PendingUpsert>();
                var deferredStates = new List<DeferredStateChange>();
                var deferredOwners = new List<DeferredOwnerChange>();
                foreach (var cdEntity in cEntities.Entities)
                {
                    var unique = cdEntity.Id.ToString();
                    SendStatus(-1, -1, totalRecords, i);
                    try
                    {
                        var oldid = cdEntity.Id;
                        var newid = Guid.Empty;

                        ReplaceGuids(container, cdEntity, includeid);
                        ReplaceUpdateInfo(cdEntity);
                        unique = GetEntityDisplayString(container, block.Import.Match, cdEntity);
                        SendStatus(null, unique);

                        if (deferStateAndOwner)
                        {
                            StripAndDeferStateOwner(cdEntity, deferredStates, deferredOwners, i, unique);
                        }

                        if (!block.TypeSpecified || block.Type == EntityTypes.Entity)
                        {
                            #region Entity

                            if (matchattributes.Count == 0)
                            {
                                if (save == SaveTypes.Never || save == SaveTypes.UpdateOnly)
                                {
                                    skipped++;
                                    SendLine(container, "{0:000} Not saving: {1}", i, unique);
                                }
                                else
                                {
                                    if (!includeid)
                                    {
                                        cdEntity.Id = Guid.Empty;
                                    }
                                    if (IsBatchable(cdEntity))
                                    {
                                        pendingCreates.Add(new PendingCreate { Entity = cdEntity, OldId = oldid, Position = i, Identifier = unique });
                                        if (pendingCreates.Count >= batchsize)
                                        {
                                            FlushPendingCreates(container, pendingCreates, ref created, ref failed, references);
                                        }
                                    }
                                    else
                                    {
                                        FlushPendingCreates(container, pendingCreates, ref created, ref failed, references);
                                        FlushPendingUpdates(container, pendingUpdates, ref updated, ref failed, references);
                                        if (SaveEntity(container, cdEntity, null, updateinactive, updateidentical, i, unique))
                                        {
                                            created++;
                                            newid = cdEntity.Id;
                                            references.Add(cdEntity.ToEntityReference());
                                        }
                                    }
                                }
                            }
                            else if (canUseUpsert && IsBatchable(cdEntity))
                            {
                                // Upsert path: skip match queries entirely, let Dataverse decide create vs update
                                pendingUpserts.Add(new PendingUpsert { Entity = cdEntity, OldId = oldid, Position = i, Identifier = unique });
                                if (pendingUpserts.Count >= batchsize)
                                {
                                    FlushPendingUpserts(container, pendingUpserts, ref created, ref updated, ref failed, references);
                                }
                                newid = cdEntity.Id;
                            }
                            else if (canUseUpsert && !IsBatchable(cdEntity))
                            {
                                // Non-batchable record in Upsert mode: flush batches and use SaveEntity
                                FlushPendingUpserts(container, pendingUpserts, ref created, ref updated, ref failed, references);
                                FlushPendingCreates(container, pendingCreates, ref created, ref failed, references);
                                FlushPendingUpdates(container, pendingUpdates, ref updated, ref failed, references);
                                if (SaveEntity(container, cdEntity, null, updateinactive, updateidentical, i, unique))
                                {
                                    // SaveEntity handles create vs update detection internally when match is null
                                    // Since we're in upsert mode with includeid=true, the record has an ID
                                    // We count this as "updated" since we don't know if it was created or updated
                                    updated++;
                                    newid = cdEntity.Id;
                                    references.Add(cdEntity.ToEntityReference());
                                }
                            }
                            else
                            {
                                // Original match-based path
                                // Flush batches before matching to ensure guidmap is up to date
                                if (pendingCreates.Count > 0)
                                {
                                    FlushPendingCreates(container, pendingCreates, ref created, ref failed, references);
                                }
                                var matches = GetMatchingRecords(container, cdEntity, matchattributes, updateattributes, preretrieveall, ref cAllRecordsToMatch);
                                if (delete == DeleteTypes.All || (matches.Count() == 1 && delete == DeleteTypes.Existing))
                                {
                                    FlushPendingUpdates(container, pendingUpdates, ref updated, ref failed, references);
                                    foreach (var cdMatch in matches.Entities)
                                    {
                                        SendLine(container, "{0:000} Deleting existing: {1}", i, unique);
                                        try
                                        {
                                            container.Delete(cdMatch);
                                            deleted++;
                                        }
                                        catch (FaultException<OrganizationServiceFault> ex)
                                        {
                                            if (ex.Message.ToUpperInvariant().Contains("DOES NOT EXIST"))
                                            {   // This may happen through cascade delete in CRM
                                                SendLine(container, "      ...already deleted");
                                            }
                                            else
                                            {
                                                throw;
                                            }
                                        }
                                    }
                                    matches.Entities.Clear();
                                }
                                if (matches.Count() == 0)
                                {
                                    if (save == SaveTypes.Never || save == SaveTypes.UpdateOnly)
                                    {
                                        skipped++;
                                        SendLine(container, "{0:000} Not creating: {1}", i, unique);
                                    }
                                    else
                                    {
                                        if (!includeid)
                                        {
                                            cdEntity.Id = Guid.Empty;
                                        }
                                        if (IsBatchable(cdEntity))
                                        {
                                            pendingCreates.Add(new PendingCreate { Entity = cdEntity, OldId = oldid, Position = i, Identifier = unique });
                                            if (pendingCreates.Count >= batchsize)
                                            {
                                                FlushPendingCreates(container, pendingCreates, ref created, ref failed, references);
                                            }
                                        }
                                        else
                                        {
                                            FlushPendingCreates(container, pendingCreates, ref created, ref failed, references);
                                            FlushPendingUpdates(container, pendingUpdates, ref updated, ref failed, references);
                                            if (SaveEntity(container, cdEntity, null, updateinactive, updateidentical, i, unique))
                                            {
                                                created++;
                                                newid = cdEntity.Id;
                                                references.Add(cdEntity.ToEntityReference());
                                            }
                                        }
                                    }
                                }
                                else if (matches.Count() == 1)
                                {
                                    var match = matches[0];
                                    newid = match.Id;
                                    if (save == SaveTypes.CreateUpdate || save == SaveTypes.UpdateOnly)
                                    {
                                        if (IsBatchable(cdEntity))
                                        {
                                            cdEntity.Id = match.Id;
                                            var primaryIdAttribute = container.Entity(cdEntity.LogicalName).PrimaryIdAttribute;
                                            var attrs = cdEntity.Attributes.Keys.ToList();
                                            if (attrs.Contains(primaryIdAttribute))
                                            {
                                                attrs.Remove(primaryIdAttribute);
                                            }
                                            if (updateidentical || !EntityAttributesEqual(container, attrs, cdEntity, match))
                                            {
                                                pendingUpdates.Add(new PendingUpdate { Entity = cdEntity, Position = i, Identifier = unique });
                                                if (pendingUpdates.Count >= batchsize)
                                                {
                                                    FlushPendingUpdates(container, pendingUpdates, ref updated, ref failed, references);
                                                }
                                            }
                                            else
                                            {
                                                skipped++;
                                                SendLine(container, "{0:000} Skipped: {1} (Identical)", i, unique);
                                            }
                                        }
                                        else
                                        {
                                            FlushPendingCreates(container, pendingCreates, ref created, ref failed, references);
                                            FlushPendingUpdates(container, pendingUpdates, ref updated, ref failed, references);
                                            if (SaveEntity(container, cdEntity, match, updateinactive, updateidentical, i, unique))
                                            {
                                                updated++;
                                                references.Add(cdEntity.ToEntityReference());
                                            }
                                            else
                                            {
                                                skipped++;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        skipped++;
                                        SendLine(container, "{0:000} Exists: {1}", i, unique);
                                    }
                                }
                                else
                                {
                                    failed++;
                                    SendLine(container, $"Import object matches {matches.Count()} records in target database!");
                                    SendLine(container, unique);
                                }
                            }
                            if (!oldid.Equals(Guid.Empty) && !newid.Equals(Guid.Empty) && !oldid.Equals(newid) && !guidmap.ContainsKey(oldid))
                            {
                                container.Log("Mapping IDs: {0} ==> {1}", oldid, newid);
                                guidmap.Add(oldid, newid);
                            }

                            if (deferStateAndOwner && !oldid.Equals(Guid.Empty) && !newid.Equals(Guid.Empty))
                            {
                                UpdateDeferredActualIds(deferredStates, deferredOwners, oldid, newid);
                            }

                            #endregion Entity
                        }
                        else if (block.Type == EntityTypes.Intersect)
                        {
                            #region Intersect

                            FlushPendingUpserts(container, pendingUpserts, ref created, ref updated, ref failed, references);
                            FlushPendingCreates(container, pendingCreates, ref created, ref failed, references);
                            FlushPendingUpdates(container, pendingUpdates, ref updated, ref failed, references);

                            if (cdEntity.Attributes.Count != 2)
                            {
                                throw new ArgumentOutOfRangeException("Attributes", cdEntity.Attributes.Count, "Invalid Attribute count for intersect object");
                            }
                            var intersect = block.IntersectName;
                            if (string.IsNullOrEmpty(intersect))
                            {
                                intersect = cdEntity.LogicalName;
                            }

                            var ref1 = GetAttributeEntityReference(cdEntity.Attributes.ElementAt(0));
                            var ref2 = GetAttributeEntityReference(cdEntity.Attributes.ElementAt(1));

                            var party1 = new Entity(ref1.LogicalName, ref1.Id);
                            var party2 = new Entity(ref2.LogicalName, ref2.Id);
                            try
                            {
                                container.Associate(party1, party2, intersect);
                                created++;
                                SendLine(container, "{0} Associated: {1}", i.ToString().PadLeft(3, '0'), name);
                            }
                            catch (Exception ex)
                            {
                                if (ex.Message.Contains("duplicate"))
                                {
                                    SendLine(container, "{0} Association exists: {1}", i.ToString().PadLeft(3, '0'), name);
                                    skipped++;
                                }
                                else
                                {
                                    throw;
                                }
                            }

                            #endregion Intersect
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        SendLine(container, $"\n*** Error record: {unique} ***\n{ex.Message}");
                        container.Log(ex);
                        if (stoponerror)
                        {
                            throw;
                        }
                    }
                    i++;
                }
                FlushPendingUpserts(container, pendingUpserts, ref created, ref updated, ref failed, references);
                FlushPendingCreates(container, pendingCreates, ref created, ref failed, references);
                FlushPendingUpdates(container, pendingUpdates, ref updated, ref failed, references);

                if (deferStateAndOwner)
                {
                    FlushDeferredStateChanges(container, deferredStates, ref updated, ref failed);
                    FlushDeferredOwnerChanges(container, deferredOwners, ref updated, ref failed);
                }

                SendLine(container, $"Created: {created} Updated: {updated} Skipped: {skipped} Deleted: {deleted} Failed: {failed}");
            }
            container.EndSection();
            return new Tuple<int, int, int, int, int, EntityReferenceCollection>(created, updated, skipped, deleted, failed, references);
        }

        private EntityReference GetAttributeEntityReference(KeyValuePair<string, object> x)
        {
            if (x.Value is EntityReference entref)
            {
                return entref;
            }
            else if (x.Value is Guid id)
            {
                if (guidmap.ContainsKey(id))
                {
                    id = guidmap[id];
                }
                var logicname = x.Key.EndsWith("id") ? x.Key.Substring(0, x.Key.Length - 2) : x.Key;
                return new EntityReference(logicname, id);
            }
            return null;
        }

        private void ReplaceGuids(IExecutionContainer container, Entity cdEntity, bool includeid)
        {
            foreach (var prop in cdEntity.Attributes)
            {
                if (prop.Value is Guid id && guidmap.ContainsKey(id))
                {
                    if (includeid)
                    {
                        throw new NotImplementedException("Cannot handle replacement of Guid type attributes");
                    }
                    else
                    {
                        container.Log("No action, we don't care about the guid of the object");
                    }
                }

                if (prop.Value is EntityReference er && guidmap.ContainsKey(er.Id))
                {
                    ((EntityReference)prop.Value).Id = guidmap[er.Id];
                }
            }
        }

        private bool SaveEntity(IExecutionContainer container, Entity cdNewEntity, Entity cdMatchEntity, bool updateInactiveRecord, bool updateIdentical, int pos, string identifier)
        {
            container.StartSection("SaveEntity " + pos.ToString("000 ") + identifier);
            var recordSaved = false;
            if (string.IsNullOrWhiteSpace(identifier))
            {
                identifier = cdNewEntity.ToString();
            }
            var newOwner = cdNewEntity.GetAttribute<EntityReference>("ownerid", null);
            var newState = cdNewEntity.GetAttribute<OptionSetValue>("statecode", null);
            var newStatus = cdNewEntity.GetAttribute<OptionSetValue>("statuscode", null);
            var newActive = newState != null ? container.GetActiveStates(cdNewEntity.LogicalName).Contains(newState.Value) : true;
            var nowActive = true;
            if ((newState == null) != (newStatus == null))
            {
                throw new InvalidDataException("When setting status of the record, both statecode and statuscode must be present");
            }
            if (!newActive)
            {
                container.Log("Removing state+status from entity to update");
                cdNewEntity.RemoveAttribute("statecode");
                cdNewEntity.RemoveAttribute("statuscode");
            }
            if (cdMatchEntity == null)
            {
                container.Create(cdNewEntity);
                recordSaved = true;
                SendLine(container, "{0:000} Created: {1}", pos, identifier);
            }
            else
            {
                var oldState = cdMatchEntity.GetAttribute<OptionSetValue>("statecode", null);
                var oldActive = oldState != null ? container.GetActiveStates(cdNewEntity.LogicalName).Contains(oldState.Value) : true;
                nowActive = oldActive;
                cdNewEntity.Id = cdMatchEntity.Id;
                if (!oldActive && (newActive || updateInactiveRecord))
                {   // Inaktiv post som ska aktiveras eller uppdateras
                    container.SetState(cdNewEntity, 0, 1);
                    SendLine(container, "{0:000} Activated: {1} for update", pos, identifier);
                    nowActive = true;
                }

                if (nowActive)
                {
                    var primaryIdAttribute = container.Entity(cdNewEntity.LogicalName).PrimaryIdAttribute;
                    var updateattributes = cdNewEntity.Attributes.Keys.ToList();
                    if (updateattributes.Contains(primaryIdAttribute))
                    {
                        updateattributes.Remove(primaryIdAttribute);
                    }
                    if (updateIdentical || !EntityAttributesEqual(container, updateattributes, cdNewEntity, cdMatchEntity))
                    {
                        try
                        {
                            container.Update(cdNewEntity);
                            recordSaved = true;
                            SendLine(container, "{0:000} Updated: {1}", pos, identifier);
                        }
                        catch (Exception ex)
                        {
                            recordSaved = false;
                            SendLine(container, "{0:000} Update Failed: {1} {2} {3}", pos, identifier, cdNewEntity.LogicalName, ex.Message);
                        }
                    }
                    else
                    {
                        SendLine(container, "{0:000} Skipped: {1} (Identical)", pos, identifier);
                    }
                }
                else
                {
                    SendLine(container, "{0:000} Inactive: {1}", pos, identifier);
                }
                if (newOwner != null && !newOwner.Equals(cdMatchEntity.GetAttribute("ownerid", new EntityReference())))
                {
                    container.Principal(cdNewEntity).On(newOwner).Assign();

                    // cdNewEntity.Assign(newOwner);
                    SendLine(container, "{0:000} Assigned: {1} to {2} {3}", pos, identifier, newOwner.LogicalName, string.IsNullOrEmpty(newOwner.Name) ? newOwner.Id.ToString() : newOwner.Name);
                }
            }
            if (newActive != nowActive)
            {   // Active should be changed on the record
                var newStatusValue = newStatus.Value;
                if (cdNewEntity.LogicalName == "savedquery" && newState.Value == 1 && newStatusValue == 1)
                {   // Adjustment for inactive but unpublished view
                    newStatusValue = 2;
                }
                if (cdNewEntity.LogicalName == "duplicaterule")
                {
                    if (newStatusValue == 2)
                    {
                        container.PublishDuplicateRule(cdNewEntity);
                        SendLine(container, "{0:000} Publish Duplicate Rule: {1}", pos, identifier);
                    }
                    else
                    {
                        container.UnpublishDuplicateRule(cdNewEntity);
                        SendLine(container, "{0:000} Unpublish Duplicate Rule: {1}", pos, identifier);
                    }
                }
                else
                {
                    container.SetState(cdNewEntity, newState.Value, newStatusValue);
                    SendLine(container, "{0:000} SetState: {1}: {2}/{3}", pos, identifier, newState.Value, newStatus.Value);
                }
            }
            container.EndSection();
            return recordSaved;
        }

        #region Batch Helpers

        private const int DefaultBatchSize = 100;

        private struct PendingCreate
        {
            public Entity Entity;
            public Guid OldId;
            public int Position;
            public string Identifier;
        }

        private struct PendingUpdate
        {
            public Entity Entity;
            public int Position;
            public string Identifier;
        }

        private struct PendingUpsert
        {
            public Entity Entity;
            public Guid OldId;
            public int Position;
            public string Identifier;
        }

        private struct DeferredStateChange
        {
            public string EntityLogicalName;
            public Guid OriginalId;      // Id from import file (for lookup)
            public Guid ActualId;        // Id after create/update (for applying state)
            public OptionSetValue StateCode;
            public OptionSetValue StatusCode;
            public int Position;
            public string Identifier;
        }

        private struct DeferredOwnerChange
        {
            public string EntityLogicalName;
            public Guid OriginalId;      // Id from import file (for lookup)
            public Guid ActualId;        // Id after create/update (for assigning owner)
            public EntityReference Owner;
            public int Position;
            public string Identifier;
        }

        /// <summary>
        /// Checks if CreateMultiple message is supported for the specified entity.
        /// Results are cached per entity logical name for the lifetime of the import run.
        /// </summary>
        /// <param name="container">The execution container.</param>
        /// <param name="entityLogicalName">The logical name of the entity to check.</param>
        /// <returns>True if CreateMultiple is supported; otherwise, false.</returns>
        private bool IsCreateMultipleSupported(IExecutionContainer container, string entityLogicalName)
        {
            return IsBulkMessageSupported(container, entityLogicalName, "CreateMultiple", createMultipleSupportCache);
        }

        /// <summary>
        /// Checks if UpdateMultiple message is supported for the specified entity.
        /// Results are cached per entity logical name for the lifetime of the import run.
        /// </summary>
        /// <param name="container">The execution container.</param>
        /// <param name="entityLogicalName">The logical name of the entity to check.</param>
        /// <returns>True if UpdateMultiple is supported; otherwise, false.</returns>
        private bool IsUpdateMultipleSupported(IExecutionContainer container, string entityLogicalName)
        {
            return IsBulkMessageSupported(container, entityLogicalName, "UpdateMultiple", updateMultipleSupportCache);
        }

        /// <summary>
        /// Checks if UpsertMultiple message is supported for the specified entity.
        /// Results are cached per entity logical name for the lifetime of the import run.
        /// </summary>
        /// <param name="container">The execution container.</param>
        /// <param name="entityLogicalName">The logical name of the entity to check.</param>
        /// <returns>True if UpsertMultiple is supported; otherwise, false.</returns>
        private bool IsUpsertMultipleSupported(IExecutionContainer container, string entityLogicalName)
        {
            return IsBulkMessageSupported(container, entityLogicalName, "UpsertMultiple", upsertMultipleSupportCache);
        }

        /// <summary>
        /// Checks if Upsert (single) message is supported for the specified entity.
        /// Results are cached per entity logical name for the lifetime of the import run.
        /// </summary>
        /// <param name="container">The execution container.</param>
        /// <param name="entityLogicalName">The logical name of the entity to check.</param>
        /// <returns>True if Upsert is supported; otherwise, false.</returns>
        private bool IsUpsertSupported(IExecutionContainer container, string entityLogicalName)
        {
            return IsBulkMessageSupported(container, entityLogicalName, "Upsert", upsertSupportCache);
        }

        /// <summary>
        /// Checks if a specific SDK message is supported for an entity by querying sdkmessagefilter.
        /// </summary>
        /// <param name="container">The execution container.</param>
        /// <param name="entityLogicalName">The logical name of the entity to check.</param>
        /// <param name="messageName">The name of the SDK message (e.g., "CreateMultiple", "UpdateMultiple").</param>
        /// <param name="cache">The cache dictionary to use for storing results.</param>
        /// <returns>True if the message is supported; otherwise, false.</returns>
        private bool IsBulkMessageSupported(IExecutionContainer container, string entityLogicalName, string messageName, Dictionary<string, bool> cache)
        {
            if (cache.TryGetValue(entityLogicalName, out var isSupported))
            {
                return isSupported;
            }

            try
            {
                var query = new QueryExpression("sdkmessagefilter")
                {
                    ColumnSet = new ColumnSet("sdkmessagefilterid"),
                    TopCount = 1,
                    Criteria = new FilterExpression
                    {
                        FilterOperator = LogicalOperator.And,
                        Conditions =
                        {
                            new ConditionExpression("primaryobjecttypecode", Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, entityLogicalName)
                        }
                    },
                    LinkEntities =
                    {
                        new LinkEntity
                        {
                            LinkFromEntityName = "sdkmessagefilter",
                            LinkToEntityName = "sdkmessage",
                            LinkFromAttributeName = "sdkmessageid",
                            LinkToAttributeName = "sdkmessageid",
                            LinkCriteria = new FilterExpression
                            {
                                Conditions =
                                {
                                    new ConditionExpression("name", Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, messageName)
                                }
                            }
                        }
                    }
                };

                var result = container.RetrieveMultiple(query);
                isSupported = result.Entities.Count > 0;
                cache[entityLogicalName] = isSupported;
                container.Log($"{messageName} support for {entityLogicalName}: {isSupported}");
                return isSupported;
            }
            catch (Exception ex)
            {
                container.Log($"Failed to check {messageName} support for {entityLogicalName}: {ex.Message}");
                cache[entityLogicalName] = false;
                return false;
            }
        }

        /// <summary>
        /// Marks CreateMultiple as unsupported for the specified entity (used when runtime execution fails).
        /// </summary>
        private void MarkCreateMultipleUnsupported(string entityLogicalName)
        {
            createMultipleSupportCache[entityLogicalName] = false;
        }

        /// <summary>
        /// Marks UpdateMultiple as unsupported for the specified entity (used when runtime execution fails).
        /// </summary>
        private void MarkUpdateMultipleUnsupported(string entityLogicalName)
        {
            updateMultipleSupportCache[entityLogicalName] = false;
        }

        /// <summary>
        /// Marks UpsertMultiple as unsupported for the specified entity (used when runtime execution fails).
        /// </summary>
        private void MarkUpsertMultipleUnsupported(string entityLogicalName)
        {
            upsertMultipleSupportCache[entityLogicalName] = false;
        }

        /// <summary>
        /// Marks Upsert (single) as unsupported for the specified entity (used when runtime execution fails).
        /// </summary>
        private void MarkUpsertUnsupported(string entityLogicalName)
        {
            upsertSupportCache[entityLogicalName] = false;
        }

        /// <summary>
        /// Checks if an exception indicates that the bulk message is not implemented (on-premises scenario).
        /// </summary>
        private static bool IsBulkMessageNotImplemented(Exception ex)
        {
            if (ex is FaultException<OrganizationServiceFault> fault)
            {
                return fault.Detail?.ErrorCode == MessageNotImplementedErrorCode;
            }
            if (ex is NotSupportedException)
            {
                return true;
            }
            if (ex.InnerException != null)
            {
                return IsBulkMessageNotImplemented(ex.InnerException);
            }
            return false;
        }

        /// <summary>
        /// Strips statecode, statuscode, and ownerid from an entity and defers them for later bulk application.
        /// </summary>
        /// <param name="entity">The entity to strip attributes from.</param>
        /// <param name="deferredStates">Collection to store deferred state changes.</param>
        /// <param name="deferredOwners">Collection to store deferred owner changes.</param>
        /// <param name="position">Record position for logging.</param>
        /// <param name="identifier">Record identifier for logging.</param>
        private void StripAndDeferStateOwner(Entity entity, List<DeferredStateChange> deferredStates, List<DeferredOwnerChange> deferredOwners, int position, string identifier)
        {
            var originalId = entity.Id;

            if (entity.Contains("statecode") && entity.Contains("statuscode"))
            {
                deferredStates.Add(new DeferredStateChange
                {
                    EntityLogicalName = entity.LogicalName,
                    OriginalId = originalId,
                    ActualId = Guid.Empty, // Will be updated after create/update
                    StateCode = entity.GetAttributeValue<OptionSetValue>("statecode"),
                    StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode"),
                    Position = position,
                    Identifier = identifier
                });
                entity.Attributes.Remove("statecode");
                entity.Attributes.Remove("statuscode");
            }

            if (entity.Contains("ownerid"))
            {
                deferredOwners.Add(new DeferredOwnerChange
                {
                    EntityLogicalName = entity.LogicalName,
                    OriginalId = originalId,
                    ActualId = Guid.Empty, // Will be updated after create/update
                    Owner = entity.GetAttributeValue<EntityReference>("ownerid"),
                    Position = position,
                    Identifier = identifier
                });
                entity.Attributes.Remove("ownerid");
            }
        }

        /// <summary>
        /// Updates the ActualId in deferred changes after a record is created or updated.
        /// </summary>
        /// <param name="deferredStates">Deferred state changes to update.</param>
        /// <param name="deferredOwners">Deferred owner changes to update.</param>
        /// <param name="originalId">The original Id from the import file.</param>
        /// <param name="actualId">The actual Id after create/update.</param>
        private void UpdateDeferredActualIds(List<DeferredStateChange> deferredStates, List<DeferredOwnerChange> deferredOwners, Guid originalId, Guid actualId)
        {
            for (int i = 0; i < deferredStates.Count; i++)
            {
                if (deferredStates[i].OriginalId == originalId)
                {
                    var item = deferredStates[i];
                    item.ActualId = actualId;
                    deferredStates[i] = item;
                }
            }

            for (int i = 0; i < deferredOwners.Count; i++)
            {
                if (deferredOwners[i].OriginalId == originalId)
                {
                    var item = deferredOwners[i];
                    item.ActualId = actualId;
                    deferredOwners[i] = item;
                }
            }
        }

        /// <summary>
        /// Applies deferred state changes in bulk using UpdateMultiple when supported.
        /// </summary>
        private void FlushDeferredStateChanges(IExecutionContainer container, List<DeferredStateChange> changes, ref int updated, ref int failed)
        {
            if (changes.Count == 0)
            {
                return;
            }

            container.Log($"Applying {changes.Count} deferred state changes");

            var byEntity = changes.GroupBy(c => c.EntityLogicalName);

            foreach (var group in byEntity)
            {
                var entityName = group.Key;
                var batch = group.ToList();

                if (entityName == "duplicaterule" || entityName == "savedquery")
                {
                    ApplyStatesIndividually(container, batch, ref updated, ref failed);
                    continue;
                }

                if (IsUpdateMultipleSupported(container, entityName))
                {
                    if (TryApplyStatesWithUpdateMultiple(container, entityName, batch, ref updated, ref failed))
                    {
                        continue;
                    }
                }

                ApplyStatesIndividually(container, batch, ref updated, ref failed);
            }
        }

        /// <summary>
        /// Attempts to apply state changes using UpdateMultiple.
        /// </summary>
        private bool TryApplyStatesWithUpdateMultiple(IExecutionContainer container, string entityName, List<DeferredStateChange> batch, ref int updated, ref int failed)
        {
            var targets = new EntityCollection { EntityName = entityName };

            foreach (var change in batch)
            {
                if (change.ActualId == Guid.Empty)
                {
                    container.Log($"WARNING: Skipping deferred state change for {change.Identifier} - ActualId not set");
                    failed++;
                    continue;
                }

                var entity = new Entity(entityName, change.ActualId);
                entity["statecode"] = change.StateCode;
                entity["statuscode"] = change.StatusCode;
                targets.Entities.Add(entity);
            }

            if (targets.Entities.Count == 0)
            {
                return true;
            }

            try
            {
                var request = new OrganizationRequest("UpdateMultiple")
                {
                    Parameters = { ["Targets"] = targets }
                };
                container.Service.Execute(request);
                updated += targets.Entities.Count;
                container.Log($"Applied {targets.Entities.Count} state changes via UpdateMultiple for {entityName}");
                return true;
            }
            catch (Exception ex)
            {
                container.Log($"UpdateMultiple for state changes failed: {ex.Message}");
                if (stoponerror)
                {
                    throw;
                }
                return false;
            }
        }

        /// <summary>
        /// Applies state changes individually using SetState.
        /// </summary>
        private void ApplyStatesIndividually(IExecutionContainer container, List<DeferredStateChange> batch, ref int updated, ref int failed)
        {
            foreach (var change in batch)
            {
                try
                {
                    if (change.ActualId == Guid.Empty)
                    {
                        failed++;
                        SendLine(container, "{0:000} SetState Failed (deferred): {1} - ActualId not set", change.Position, change.Identifier);
                        if (stoponerror)
                        {
                            throw new InvalidOperationException($"ActualId not set for deferred state change on {change.Identifier}");
                        }
                        continue;
                    }

                    var entity = new Entity(change.EntityLogicalName, change.ActualId);

                    if (change.EntityLogicalName == "savedquery" && change.StateCode.Value == 1 && change.StatusCode.Value == 1)
                    {
                        container.SetState(entity, 1, 2);
                    }
                    else if (change.EntityLogicalName == "duplicaterule")
                    {
                        if (change.StatusCode.Value == 2)
                        {
                            container.PublishDuplicateRule(entity);
                        }
                        else
                        {
                            container.UnpublishDuplicateRule(entity);
                        }
                    }
                    else
                    {
                        container.SetState(entity, change.StateCode.Value, change.StatusCode.Value);
                    }

                    updated++;
                    SendLine(container, "{0:000} SetState (deferred): {1}: {2}/{3}", change.Position, change.Identifier, change.StateCode.Value, change.StatusCode.Value);
                }
                catch (Exception ex)
                {
                    failed++;
                    SendLine(container, "{0:000} SetState Failed (deferred): {1} {2}", change.Position, change.Identifier, ex.Message);
                    if (stoponerror)
                    {
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Applies deferred owner changes in bulk when possible.
        /// </summary>
        private void FlushDeferredOwnerChanges(IExecutionContainer container, List<DeferredOwnerChange> changes, ref int updated, ref int failed)
        {
            if (changes.Count == 0)
            {
                return;
            }

            container.Log($"Applying {changes.Count} deferred owner changes");

            foreach (var change in changes)
            {
                try
                {
                    if (change.ActualId == Guid.Empty)
                    {
                        failed++;
                        SendLine(container, "{0:000} Assign Failed (deferred): {1} - ActualId not set", change.Position, change.Identifier);
                        if (stoponerror)
                        {
                            throw new InvalidOperationException($"ActualId not set for deferred owner change on {change.Identifier}");
                        }
                        continue;
                    }

                    var entity = new Entity(change.EntityLogicalName, change.ActualId);
                    container.Principal(entity).On(change.Owner).Assign();
                    updated++;
                    SendLine(container, "{0:000} Assigned (deferred): {1} to {2} {3}", change.Position, change.Identifier, change.Owner.LogicalName, string.IsNullOrEmpty(change.Owner.Name) ? change.Owner.Id.ToString() : change.Owner.Name);
                }
                catch (Exception ex)
                {
                    failed++;
                    SendLine(container, "{0:000} Assign Failed (deferred): {1} {2}", change.Position, change.Identifier, ex.Message);
                    if (stoponerror)
                    {
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Flushes pending create operations using CreateMultiple when supported, falling back to ExecuteMultiple or individual calls.
        /// </summary>
        /// <param name="container">The execution container.</param>
        /// <param name="batch">The batch of pending create operations.</param>
        /// <param name="created">Counter for successfully created records.</param>
        /// <param name="failed">Counter for failed records.</param>
        /// <param name="references">Collection to store created entity references.</param>
        private void FlushPendingCreates(IExecutionContainer container, List<PendingCreate> batch, ref int created, ref int failed, EntityReferenceCollection references)
        {
            if (batch.Count == 0)
            {
                return;
            }

            if (batch.Count == 1)
            {
                FlushSingleCreate(container, batch[0], ref created, ref failed, references);
                batch.Clear();
                return;
            }

            var entityLogicalName = batch[0].Entity.LogicalName;

            if (IsCreateMultipleSupported(container, entityLogicalName))
            {
                if (TryFlushCreatesWithCreateMultiple(container, batch, ref created, ref failed, references))
                {
                    batch.Clear();
                    return;
                }
            }

            FlushCreatesWithExecuteMultiple(container, batch, ref created, ref failed, references);
            batch.Clear();
        }

        /// <summary>
        /// Creates a single record.
        /// </summary>
        private void FlushSingleCreate(IExecutionContainer container, PendingCreate item, ref int created, ref int failed, EntityReferenceCollection references)
        {
            try
            {
                container.Create(item.Entity);
                created++;
                SendLine(container, "{0:000} Created: {1}", item.Position, item.Identifier);
                references.Add(item.Entity.ToEntityReference());
                MapGuid(item.OldId, item.Entity.Id);
            }
            catch (Exception ex)
            {
                failed++;
                SendLine(container, "{0:000} Create Failed: {1} {2}", item.Position, item.Identifier, ex.Message);
                if (stoponerror)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Attempts to flush creates using CreateMultipleRequest (Dataverse bulk API).
        /// Returns true if successful; false if the message is not supported and fallback is needed.
        /// </summary>
        private bool TryFlushCreatesWithCreateMultiple(IExecutionContainer container, List<PendingCreate> batch, ref int created, ref int failed, EntityReferenceCollection references)
        {
            var entityLogicalName = batch[0].Entity.LogicalName;
            var targets = new EntityCollection { EntityName = entityLogicalName };
            foreach (var item in batch)
            {
                targets.Entities.Add(item.Entity);
            }

            var request = new OrganizationRequest("CreateMultiple")
            {
                Parameters = { ["Targets"] = targets }
            };

            container.Log($"Executing CreateMultiple for {batch.Count} {entityLogicalName} records");

            try
            {
                var response = container.Service.Execute(request);
                var createdIds = response.Results.Contains("Ids") ? (Guid[])response.Results["Ids"] : null;

                for (var i = 0; i < batch.Count; i++)
                {
                    var item = batch[i];
                    if (createdIds != null && i < createdIds.Length)
                    {
                        item.Entity.Id = createdIds[i];
                    }
                    created++;
                    SendLine(container, "{0:000} Created: {1}", item.Position, item.Identifier);
                    references.Add(item.Entity.ToEntityReference());
                    MapGuid(item.OldId, item.Entity.Id);
                }
                return true;
            }
            catch (Exception ex)
            {
                container.Log($"CreateMultiple failed: {ex.Message}");

                if (IsBulkMessageNotImplemented(ex))
                {
                    container.Log("CreateMultiple not implemented, marking as unsupported and falling back");
                    MarkCreateMultipleUnsupported(entityLogicalName);
                    return false;
                }

                container.Log("CreateMultiple batch failed, falling back to individual creates");
                if (stoponerror)
                {
                    throw;
                }

                FlushCreatesIndividually(container, batch, ref created, ref failed, references);
                return true;
            }
        }

        /// <summary>
        /// Flushes creates using ExecuteMultipleRequest (legacy batch approach).
        /// </summary>
        private void FlushCreatesWithExecuteMultiple(IExecutionContainer container, List<PendingCreate> batch, ref int created, ref int failed, EntityReferenceCollection references)
        {
            var multiRequest = new ExecuteMultipleRequest
            {
                Requests = new OrganizationRequestCollection(),
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = !stoponerror,
                    ReturnResponses = true
                }
            };

            foreach (var item in batch)
            {
                multiRequest.Requests.Add(new CreateRequest { Target = item.Entity });
            }

            container.Log($"Executing ExecuteMultiple batch create of {batch.Count} records");

            try
            {
                var multiResponse = (ExecuteMultipleResponse)container.Service.Execute(multiRequest);

                for (var i = 0; i < batch.Count; i++)
                {
                    var item = batch[i];
                    var responseItem = multiResponse.Responses.FirstOrDefault(r => r.RequestIndex == i);

                    if (responseItem?.Fault != null)
                    {
                        failed++;
                        SendLine(container, "{0:000} Create Failed: {1} {2}", item.Position, item.Identifier, responseItem.Fault.Message);
                    }
                    else
                    {
                        if (responseItem?.Response is CreateResponse createResponse)
                        {
                            item.Entity.Id = createResponse.id;
                        }
                        created++;
                        SendLine(container, "{0:000} Created: {1}", item.Position, item.Identifier);
                        references.Add(item.Entity.ToEntityReference());
                        MapGuid(item.OldId, item.Entity.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                container.Log($"ExecuteMultiple batch create failed: {ex.Message}");
                container.Log("Falling back to sequential creates");
                FlushCreatesIndividually(container, batch, ref created, ref failed, references);
            }
        }

        /// <summary>
        /// Flushes creates individually (used as fallback when batch operations fail).
        /// </summary>
        private void FlushCreatesIndividually(IExecutionContainer container, List<PendingCreate> batch, ref int created, ref int failed, EntityReferenceCollection references)
        {
            foreach (var item in batch)
            {
                try
                {
                    container.Create(item.Entity);
                    created++;
                    SendLine(container, "{0:000} Created: {1}", item.Position, item.Identifier);
                    references.Add(item.Entity.ToEntityReference());
                    MapGuid(item.OldId, item.Entity.Id);
                }
                catch (Exception itemEx)
                {
                    failed++;
                    SendLine(container, "{0:000} Create Failed: {1} {2}", item.Position, item.Identifier, itemEx.Message);
                    if (stoponerror)
                    {
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Flushes pending update operations using UpdateMultiple when supported, falling back to ExecuteMultiple or individual calls.
        /// </summary>
        /// <param name="container">The execution container.</param>
        /// <param name="batch">The batch of pending update operations.</param>
        /// <param name="updated">Counter for successfully updated records.</param>
        /// <param name="failed">Counter for failed records.</param>
        /// <param name="references">Collection to store updated entity references.</param>
        private void FlushPendingUpdates(IExecutionContainer container, List<PendingUpdate> batch, ref int updated, ref int failed, EntityReferenceCollection references)
        {
            if (batch.Count == 0)
            {
                return;
            }

            if (batch.Count == 1)
            {
                FlushSingleUpdate(container, batch[0], ref updated, ref failed, references);
                batch.Clear();
                return;
            }

            var entityLogicalName = batch[0].Entity.LogicalName;

            if (IsUpdateMultipleSupported(container, entityLogicalName))
            {
                if (TryFlushUpdatesWithUpdateMultiple(container, batch, ref updated, ref failed, references))
                {
                    batch.Clear();
                    return;
                }
            }

            FlushUpdatesWithExecuteMultiple(container, batch, ref updated, ref failed, references);
            batch.Clear();
        }

        /// <summary>
        /// Updates a single record.
        /// </summary>
        private void FlushSingleUpdate(IExecutionContainer container, PendingUpdate item, ref int updated, ref int failed, EntityReferenceCollection references)
        {
            try
            {
                container.Update(item.Entity);
                updated++;
                SendLine(container, "{0:000} Updated: {1}", item.Position, item.Identifier);
                references.Add(item.Entity.ToEntityReference());
            }
            catch (Exception ex)
            {
                failed++;
                SendLine(container, "{0:000} Update Failed: {1} {2} {3}", item.Position, item.Identifier, item.Entity.LogicalName, ex.Message);
                if (stoponerror)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Attempts to flush updates using UpdateMultipleRequest (Dataverse bulk API).
        /// Returns true if successful; false if the message is not supported and fallback is needed.
        /// </summary>
        private bool TryFlushUpdatesWithUpdateMultiple(IExecutionContainer container, List<PendingUpdate> batch, ref int updated, ref int failed, EntityReferenceCollection references)
        {
            var entityLogicalName = batch[0].Entity.LogicalName;
            var targets = new EntityCollection { EntityName = entityLogicalName };
            foreach (var item in batch)
            {
                targets.Entities.Add(item.Entity);
            }

            var request = new OrganizationRequest("UpdateMultiple")
            {
                Parameters = { ["Targets"] = targets }
            };

            container.Log($"Executing UpdateMultiple for {batch.Count} {entityLogicalName} records");

            try
            {
                container.Service.Execute(request);

                foreach (var item in batch)
                {
                    updated++;
                    SendLine(container, "{0:000} Updated: {1}", item.Position, item.Identifier);
                    references.Add(item.Entity.ToEntityReference());
                }
                return true;
            }
            catch (Exception ex)
            {
                container.Log($"UpdateMultiple failed: {ex.Message}");

                if (IsBulkMessageNotImplemented(ex))
                {
                    container.Log("UpdateMultiple not implemented, marking as unsupported and falling back");
                    MarkUpdateMultipleUnsupported(entityLogicalName);
                    return false;
                }

                container.Log("UpdateMultiple batch failed, falling back to individual updates");
                if (stoponerror)
                {
                    throw;
                }

                FlushUpdatesIndividually(container, batch, ref updated, ref failed, references);
                return true;
            }
        }

        /// <summary>
        /// Flushes updates using ExecuteMultipleRequest (legacy batch approach).
        /// </summary>
        private void FlushUpdatesWithExecuteMultiple(IExecutionContainer container, List<PendingUpdate> batch, ref int updated, ref int failed, EntityReferenceCollection references)
        {
            var multiRequest = new ExecuteMultipleRequest
            {
                Requests = new OrganizationRequestCollection(),
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = !stoponerror,
                    ReturnResponses = true
                }
            };

            foreach (var item in batch)
            {
                multiRequest.Requests.Add(new UpdateRequest { Target = item.Entity });
            }

            container.Log($"Executing ExecuteMultiple batch update of {batch.Count} records");

            try
            {
                var multiResponse = (ExecuteMultipleResponse)container.Service.Execute(multiRequest);

                for (var i = 0; i < batch.Count; i++)
                {
                    var item = batch[i];
                    var responseItem = multiResponse.Responses.FirstOrDefault(r => r.RequestIndex == i);

                    if (responseItem?.Fault != null)
                    {
                        failed++;
                        SendLine(container, "{0:000} Update Failed: {1} {2} {3}", item.Position, item.Identifier, item.Entity.LogicalName, responseItem.Fault.Message);
                    }
                    else
                    {
                        updated++;
                        SendLine(container, "{0:000} Updated: {1}", item.Position, item.Identifier);
                        references.Add(item.Entity.ToEntityReference());
                    }
                }
            }
            catch (Exception ex)
            {
                container.Log($"ExecuteMultiple batch update failed: {ex.Message}");
                container.Log("Falling back to sequential updates");
                FlushUpdatesIndividually(container, batch, ref updated, ref failed, references);
            }
        }

        /// <summary>
        /// Flushes updates individually (used as fallback when batch operations fail).
        /// </summary>
        private void FlushUpdatesIndividually(IExecutionContainer container, List<PendingUpdate> batch, ref int updated, ref int failed, EntityReferenceCollection references)
        {
            foreach (var item in batch)
            {
                try
                {
                    container.Update(item.Entity);
                    updated++;
                    SendLine(container, "{0:000} Updated: {1}", item.Position, item.Identifier);
                    references.Add(item.Entity.ToEntityReference());
                }
                catch (Exception itemEx)
                {
                    failed++;
                    SendLine(container, "{0:000} Update Failed: {1} {2} {3}", item.Position, item.Identifier, item.Entity.LogicalName, itemEx.Message);
                    if (stoponerror)
                    {
                        throw;
                    }
                }
            }
        }

        #region Upsert Operations

        /// <summary>
        /// Flushes pending upsert operations using UpsertMultiple when supported, falling back to ExecuteMultiple with Upsert, 
        /// then individual Upsert, then individual Create/Update.
        /// </summary>
        /// <param name="container">The execution container.</param>
        /// <param name="batch">The batch of pending upsert operations.</param>
        /// <param name="created">Counter for successfully created records.</param>
        /// <param name="updated">Counter for successfully updated records.</param>
        /// <param name="failed">Counter for failed records.</param>
        /// <param name="references">Collection to store entity references.</param>
        private void FlushPendingUpserts(IExecutionContainer container, List<PendingUpsert> batch, ref int created, ref int updated, ref int failed, EntityReferenceCollection references)
        {
            if (batch.Count == 0)
            {
                return;
            }

            if (batch.Count == 1)
            {
                FlushSingleUpsert(container, batch[0], ref created, ref updated, ref failed, references);
                batch.Clear();
                return;
            }

            var entityLogicalName = batch[0].Entity.LogicalName;

            if (IsUpsertMultipleSupported(container, entityLogicalName))
            {
                if (TryFlushUpsertsWithUpsertMultiple(container, batch, ref created, ref updated, ref failed, references))
                {
                    batch.Clear();
                    return;
                }
            }

            if (IsUpsertSupported(container, entityLogicalName))
            {
                if (TryFlushUpsertsWithExecuteMultiple(container, batch, ref created, ref updated, ref failed, references))
                {
                    batch.Clear();
                    return;
                }
            }

            // Final fallback: individual Create/Update operations
            FlushUpsertsAsCreateUpdate(container, batch, ref created, ref updated, ref failed, references);
            batch.Clear();
        }

        /// <summary>
        /// Upserts a single record.
        /// </summary>
        private void FlushSingleUpsert(IExecutionContainer container, PendingUpsert item, ref int created, ref int updated, ref int failed, EntityReferenceCollection references)
        {
            var entityLogicalName = item.Entity.LogicalName;

            if (IsUpsertSupported(container, entityLogicalName))
            {
                try
                {
                    var request = new UpsertRequest { Target = item.Entity };
                    var response = (UpsertResponse)container.Service.Execute(request);

                    if (response.RecordCreated)
                    {
                        if (response.Target != null)
                        {
                            item.Entity.Id = response.Target.Id;
                        }
                        created++;
                        SendLine(container, "{0:000} Created (upsert): {1}", item.Position, item.Identifier);
                    }
                    else
                    {
                        updated++;
                        SendLine(container, "{0:000} Updated (upsert): {1}", item.Position, item.Identifier);
                    }
                    references.Add(item.Entity.ToEntityReference());
                    MapGuid(item.OldId, item.Entity.Id);
                    return;
                }
                catch (Exception ex)
                {
                    if (IsBulkMessageNotImplemented(ex))
                    {
                        container.Log($"Upsert not implemented for {entityLogicalName}, falling back to Create/Update");
                        MarkUpsertUnsupported(entityLogicalName);
                    }
                    else
                    {
                        failed++;
                        SendLine(container, "{0:000} Upsert Failed: {1} {2}", item.Position, item.Identifier, ex.Message);
                        if (stoponerror)
                        {
                            throw;
                        }
                        return;
                    }
                }
            }

            // Fallback to Create (since we don't have a match for single upsert fallback)
            try
            {
                container.Create(item.Entity);
                created++;
                SendLine(container, "{0:000} Created: {1}", item.Position, item.Identifier);
                references.Add(item.Entity.ToEntityReference());
                MapGuid(item.OldId, item.Entity.Id);
            }
            catch (Exception ex)
            {
                failed++;
                SendLine(container, "{0:000} Create Failed: {1} {2}", item.Position, item.Identifier, ex.Message);
                if (stoponerror)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Attempts to flush upserts using UpsertMultipleRequest (Dataverse bulk API).
        /// Returns true if successful; false if the message is not supported and fallback is needed.
        /// </summary>
        private bool TryFlushUpsertsWithUpsertMultiple(IExecutionContainer container, List<PendingUpsert> batch, ref int created, ref int updated, ref int failed, EntityReferenceCollection references)
        {
            var entityLogicalName = batch[0].Entity.LogicalName;
            var targets = new EntityCollection { EntityName = entityLogicalName };
            foreach (var item in batch)
            {
                targets.Entities.Add(item.Entity);
            }

            var request = new OrganizationRequest("UpsertMultiple")
            {
                Parameters = { ["Targets"] = targets }
            };

            container.Log($"Executing UpsertMultiple for {batch.Count} {entityLogicalName} records");

            try
            {
                var response = container.Service.Execute(request);
                var results = response.Results.Contains("Results") ? (UpsertResponse[])response.Results["Results"] : null;

                for (var i = 0; i < batch.Count; i++)
                {
                    var item = batch[i];
                    var upsertResult = results != null && i < results.Length ? results[i] : null;

                    if (upsertResult != null)
                    {
                        if (upsertResult.RecordCreated)
                        {
                            if (upsertResult.Target != null)
                            {
                                item.Entity.Id = upsertResult.Target.Id;
                            }
                            created++;
                            SendLine(container, "{0:000} Created (upsert): {1}", item.Position, item.Identifier);
                        }
                        else
                        {
                            updated++;
                            SendLine(container, "{0:000} Updated (upsert): {1}", item.Position, item.Identifier);
                        }
                    }
                    else
                    {
                        // If no result available, count as updated (default upsert behavior)
                        updated++;
                        SendLine(container, "{0:000} Upserted: {1}", item.Position, item.Identifier);
                    }
                    references.Add(item.Entity.ToEntityReference());
                    MapGuid(item.OldId, item.Entity.Id);
                }
                return true;
            }
            catch (Exception ex)
            {
                container.Log($"UpsertMultiple failed: {ex.Message}");

                if (IsBulkMessageNotImplemented(ex))
                {
                    container.Log("UpsertMultiple not implemented, marking as unsupported and falling back");
                    MarkUpsertMultipleUnsupported(entityLogicalName);
                    return false;
                }

                container.Log("UpsertMultiple batch failed, falling back to ExecuteMultiple with Upsert");
                if (stoponerror)
                {
                    throw;
                }

                // Try ExecuteMultiple with individual Upsert requests
                return TryFlushUpsertsWithExecuteMultiple(container, batch, ref created, ref updated, ref failed, references);
            }
        }

        /// <summary>
        /// Flushes upserts using ExecuteMultipleRequest with individual UpsertRequest items.
        /// Returns true if successful; false if Upsert is not supported and fallback is needed.
        /// </summary>
        private bool TryFlushUpsertsWithExecuteMultiple(IExecutionContainer container, List<PendingUpsert> batch, ref int created, ref int updated, ref int failed, EntityReferenceCollection references)
        {
            var entityLogicalName = batch[0].Entity.LogicalName;
            var multiRequest = new ExecuteMultipleRequest
            {
                Requests = new OrganizationRequestCollection(),
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = !stoponerror,
                    ReturnResponses = true
                }
            };

            foreach (var item in batch)
            {
                multiRequest.Requests.Add(new UpsertRequest { Target = item.Entity });
            }

            container.Log($"Executing ExecuteMultiple with UpsertRequest for {batch.Count} {entityLogicalName} records");

            try
            {
                var multiResponse = (ExecuteMultipleResponse)container.Service.Execute(multiRequest);

                var upsertNotImplemented = false;
                for (var i = 0; i < batch.Count; i++)
                {
                    var item = batch[i];
                    var responseItem = multiResponse.Responses.FirstOrDefault(r => r.RequestIndex == i);

                    if (responseItem?.Fault != null)
                    {
                        // Check if fault indicates Upsert not implemented
                        if (responseItem.Fault.ErrorCode == MessageNotImplementedErrorCode)
                        {
                            upsertNotImplemented = true;
                            break;
                        }
                        failed++;
                        SendLine(container, "{0:000} Upsert Failed: {1} {2}", item.Position, item.Identifier, responseItem.Fault.Message);
                    }
                    else if (responseItem?.Response is UpsertResponse upsertResponse)
                    {
                        if (upsertResponse.RecordCreated)
                        {
                            if (upsertResponse.Target != null)
                            {
                                item.Entity.Id = upsertResponse.Target.Id;
                            }
                            created++;
                            SendLine(container, "{0:000} Created (upsert): {1}", item.Position, item.Identifier);
                        }
                        else
                        {
                            updated++;
                            SendLine(container, "{0:000} Updated (upsert): {1}", item.Position, item.Identifier);
                        }
                        references.Add(item.Entity.ToEntityReference());
                        MapGuid(item.OldId, item.Entity.Id);
                    }
                }

                if (upsertNotImplemented)
                {
                    container.Log("Upsert not implemented, marking as unsupported and falling back to Create/Update");
                    MarkUpsertUnsupported(entityLogicalName);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                container.Log($"ExecuteMultiple with Upsert failed: {ex.Message}");

                if (IsBulkMessageNotImplemented(ex))
                {
                    container.Log("Upsert not implemented, marking as unsupported and falling back");
                    MarkUpsertUnsupported(entityLogicalName);
                    return false;
                }

                container.Log("Falling back to individual Create/Update operations");
                FlushUpsertsAsCreateUpdate(container, batch, ref created, ref updated, ref failed, references);
                return true;
            }
        }

        /// <summary>
        /// Flushes upserts using individual Create/Update operations (final fallback for CRM 8.x and older).
        /// Since we don't have match results, we attempt Create first and fall back to Update on duplicate key error.
        /// </summary>
        private void FlushUpsertsAsCreateUpdate(IExecutionContainer container, List<PendingUpsert> batch, ref int created, ref int updated, ref int failed, EntityReferenceCollection references)
        {
            container.Log($"Falling back to Create/Update for {batch.Count} records (Upsert not available)");

            foreach (var item in batch)
            {
                try
                {
                    // Attempt Create first
                    container.Create(item.Entity);
                    created++;
                    SendLine(container, "{0:000} Created: {1}", item.Position, item.Identifier);
                    references.Add(item.Entity.ToEntityReference());
                    MapGuid(item.OldId, item.Entity.Id);
                }
                catch (FaultException<OrganizationServiceFault> createEx)
                {
                    // Check for duplicate key error (record exists)
                    if (createEx.Detail?.ErrorCode == -2147220937 || // DuplicateRecordEntityKey
                        createEx.Detail?.ErrorCode == -2147220685 || // DuplicateRecord
                        createEx.Message.Contains("duplicate") ||
                        createEx.Message.Contains("already exists"))
                    {
                        // Record exists, try Update instead
                        try
                        {
                            container.Update(item.Entity);
                            updated++;
                            SendLine(container, "{0:000} Updated: {1}", item.Position, item.Identifier);
                            references.Add(item.Entity.ToEntityReference());
                            MapGuid(item.OldId, item.Entity.Id);
                        }
                        catch (Exception updateEx)
                        {
                            failed++;
                            SendLine(container, "{0:000} Update Failed (fallback): {1} {2}", item.Position, item.Identifier, updateEx.Message);
                            if (stoponerror)
                            {
                                throw;
                            }
                        }
                    }
                    else
                    {
                        failed++;
                        SendLine(container, "{0:000} Create Failed: {1} {2}", item.Position, item.Identifier, createEx.Message);
                        if (stoponerror)
                        {
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    SendLine(container, "{0:000} Create Failed: {1} {2}", item.Position, item.Identifier, ex.Message);
                    if (stoponerror)
                    {
                        throw;
                    }
                }
            }
        }

        #endregion Upsert Operations

        private void FlushPendingDeletes(IExecutionContainer container, List<Entity> batch, ref int deleted, ref int failed)
        {
            if (batch.Count == 0)
            {
                return;
            }
            if (batch.Count == 1)
            {
                container.Delete(batch[0]);
                deleted++;
                batch.Clear();
                return;
            }
            var multiRequest = new ExecuteMultipleRequest
            {
                Requests = new OrganizationRequestCollection(),
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = !stoponerror,
                    ReturnResponses = true
                }
            };
            foreach (var entity in batch)
            {
                multiRequest.Requests.Add(new DeleteRequest { Target = entity.ToEntityReference() });
            }
            container.Log($"Executing batch delete of {batch.Count} records");
            try
            {
                var multiResponse = (ExecuteMultipleResponse)container.Service.Execute(multiRequest);
                for (var i = 0; i < batch.Count; i++)
                {
                    var responseItem = multiResponse.Responses.FirstOrDefault(r => r.RequestIndex == i);
                    if (responseItem?.Fault != null)
                    {
                        if (responseItem.Fault.Message.ToUpperInvariant().Contains("DOES NOT EXIST"))
                        {
                            SendLine(container, "      ...already deleted");
                        }
                        else
                        {
                            failed++;
                            SendLine(container, "Delete Failed: {0} {1}", batch[i].LogicalName, responseItem.Fault.Message);
                        }
                    }
                    else
                    {
                        deleted++;
                    }
                }
            }
            catch (Exception ex)
            {
                container.Log($"Batch delete failed: {ex.Message}");
                container.Log("Falling back to sequential deletes");
                foreach (var entity in batch)
                {
                    try
                    {
                        container.Delete(entity);
                        deleted++;
                    }
                    catch (FaultException<OrganizationServiceFault> fex)
                    {
                        if (fex.Message.ToUpperInvariant().Contains("DOES NOT EXIST"))
                        {
                            SendLine(container, "      ...already deleted");
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }
            batch.Clear();
        }

        private void MapGuid(Guid oldId, Guid newId)
        {
            if (!oldId.Equals(Guid.Empty) && !newId.Equals(Guid.Empty) && !oldId.Equals(newId) && !guidmap.ContainsKey(oldId))
            {
                guidmap.Add(oldId, newId);
            }
        }

        /// <summary>
        /// Determines if a record can be saved with a simple Create or Update (no state changes, no owner reassignment).
        /// </summary>
        private static bool IsBatchable(Entity entity)
        {
            return !entity.Contains("statecode") && !entity.Contains("statuscode") && !entity.Contains("ownerid");
        }

        #endregion Batch Helpers

        #endregion Private Methods
    }

    internal static class DuplicateRuleExt
    {
        public static void UnpublishDuplicateRule(this IExecutionContainer container, Entity duplicateRule) => container.Execute(new UnpublishDuplicateRuleRequest { DuplicateRuleId = duplicateRule.Id });

        public static void PublishDuplicateRule(this IExecutionContainer container, Entity duplicateRule) => container.Execute(new PublishDuplicateRuleRequest { DuplicateRuleId = duplicateRule.Id });
    }
}