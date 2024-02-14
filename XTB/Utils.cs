using System;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
using System.Windows.Forms;

namespace Rappen.XTB.Shuffle
{
    public static class Utils
    {
        private static NameValueCollection commonparams = new NameValueCollection { { "utm_source", "Shuffle" }, { "utm_medium", "XrmToolBox" } };
        private static NameValueCollection microsoftparams = new NameValueCollection { { "WT.mc_id", "DX-MVP-5002475" } };

        public static void OpenURL(string url)
        {
            if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri _))
            {
                return;
            }
            var urib = new UriBuilder(url);
            var qry = HttpUtility.ParseQueryString(urib.Query);
            if (urib.Host.ToLowerInvariant().Contains("microsoft.com"))
            {
                microsoftparams.AllKeys.ToList().ForEach(k => qry[k] = microsoftparams[k]);
                urib.Path = urib.Path.Replace("/en-us/", "/");
            }
            commonparams.AllKeys.ToList().ForEach(k => qry[k] = commonparams[k]);

            urib.Query = qry.ToString();
            System.Diagnostics.Process.Start(urib.Uri.ToString());
        }

        public static void OpenControlURL(object sender)
        {
            if (sender is Control control)
            {
                if (control.Tag is string tag && tag.ToLowerInvariant().StartsWith("http"))
                {
                    OpenURL(tag);
                }
                else if (control.Text is string text && text.ToLowerInvariant().StartsWith("http"))
                {
                    OpenURL(text);
                }
            }
        }
    }
}