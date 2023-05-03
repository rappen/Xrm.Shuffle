using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Rappen.XTB.Shuffle
{
    public partial class About : Form
    {
        //      private CustomActionTester cat;

        public About(string sender)
        {
            InitializeComponent();
            //     this.cat = cat;
            switch (sender)
            {
                case "B":
                    imgLogo.Image = Properties.Resources.Shuffle_B_720;
                    lblToolname.Text = "Shuffle Builder";
                    break;

                case "R":
                    imgLogo.Image = Properties.Resources.Shuffle_R_720;
                    lblToolname.Text = "Shuffle Runner";
                    break;

                case "D":
                    imgLogo.Image = Properties.Resources.Shuffle_D_720;
                    lblToolname.Text = "Shuffle Deployer";
                    break;
            }
            PopulateAssemblies();
        }

        private void PopulateAssemblies()
        {
            var assemblies = GetReferencedAssemblies();
            var items = assemblies.Select(a => GetListItem(a)).ToArray();
            listAssemblies.Items.Clear();
            listAssemblies.Items.AddRange(items);
        }

        private ListViewItem GetListItem(AssemblyName a)
        {
            var item = new ListViewItem(a.Name);
            item.SubItems.Add(a.Version.ToString());
            return item;
        }

        private List<AssemblyName> GetReferencedAssemblies()
        {
            var names = Assembly.GetExecutingAssembly().GetReferencedAssemblies()
                .Where(a => !a.Name.Equals("mscorlib") && !a.Name.StartsWith("System") && !a.Name.Contains("CSharp")).ToList();
            names.Add(Assembly.GetEntryAssembly().GetName());
            names.Add(Assembly.GetExecutingAssembly().GetName());
            names = names.OrderBy(a => assemblyPrioritizer(a.Name)).ToList();
            return names;
        }

        private static string assemblyPrioritizer(string assemblyName)
        {
            return
                assemblyName.Equals("XrmToolBox") ? "AAAAAAAAAAAA" :
                assemblyName.Contains("XrmToolBox") ? "AAAAAAAAAAABa" :
                assemblyName.Contains("McTools") ? "AAAAAAAAAAABb" :
                assemblyName.Equals(Assembly.GetExecutingAssembly().GetName().Name) ? "AAAAAAAAAAAC" :
                assemblyName.Contains("Jonas") ? "AAAAAAAAAAAD" :
                assemblyName.Contains("Rappen") ? "AAAAAAAAAAAE" :
                assemblyName.Contains("Innofactor") ? "AAAAAAAAAAAF" :
                assemblyName.Contains("Cinteros") ? "AAAAAAAAAAAG" :
                assemblyName;
        }

        private void link_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Utils.OpenControlURL(sender);
        }
    }
}