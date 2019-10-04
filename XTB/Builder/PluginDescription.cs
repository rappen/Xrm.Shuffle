﻿namespace Innofactor.Crm.Shuffle.Builder
{
    using Innofactor.Crm.Shuffle.XTB;
    using System.ComponentModel.Composition;
    using XrmToolBox.Extensibility;
    using XrmToolBox.Extensibility.Interfaces;

    [Export(typeof(IXrmToolBoxPlugin)),
        ExportMetadata("Name", "Shuffle Builder"),
        ExportMetadata("Description", "Build definition files for the Shuffle"),
        ExportMetadata("SmallImageBase64", "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAIAAAD8GO2jAAAAFXRFWHRDcmVhdGlvbiBUaW1lAAfhBQsOMSbF4ibDAAAAB3RJTUUH4QULDwIfuk9CvAAAAAlwSFlzAAALEgAACxIB0t1+/AAABL1JREFUeNrtVG1MW1UY7vcHpd9AaUtbuhZodaUwmBtjwpbJ5mZ0UzPFLeqP6eJi/ExMpiSTaGKWubgf/FOJP8TFEBfnELOh0wmbQ10Zlc+y2paWclvaQj9o4fbetr7X21AGi1vM8If6pjm95z3Ped/zPs97DjWTyVDW0mhrGv2fSMC46xFt3chUf4grZZXvljt/mKGuhQbWjkkWn4EMhKPTC9kKZkYiI2e8dCbV1Kyec8YdF/21h3UiTd7qzTiasrQ7OUKWYa9ioN0pVHGnLXPScn71s6UrkMVmIV/BzWrQe9yWxtJoDO9vs/PlHP9QFEvgtzzd9EDYft4v0fE8V4LOSwGJLp9CoYZdiSVAMo5TaVQwdX0BK5+eTQDhVFukFQ/J03iaLWD+RflwDhgVG8QpPENjUIvNIrbgJiFZPEblAbXhEUW+jGM+qKG3traC19UbWAxhhr1K3QNFKTQ90Y1A4fZv/VJ9vvW0e+xLr6JWfO55C8SCpYVQkkqn2i/40TDG5jPiATSNZ1R1kr4TNsuHTmBYUSOiMbJHz/7Vv16+GMG6jliWitU1ybhilr3HX2gUJIJJNp+ZwgkOtduKYBU6RLutkM6irdshI/Huy0FQtaHF4Pox4LNGlgrKJgAqd52sVNQSpyA9ihoxiAyMMbl0AkenArPwQRLCzKMTTFIp5CpBfSIFYMtHTrIRViY494IlEURllUI0iv3t7oSCGt4yVDwsB/ZXJgAe/b9FIu4E6IbGiBzABowxZBG2oRHM8f0MaJOMYaQfMPCdSWeS8zh4YAQ+QQkgx9aFLIZzp8yKDCUPd07FkAXzQTWWSMEe2MCVsMKTibLdxdAYk5dD0PIUYIloIgpfyZ1zxKl0mkjDQwbDwJ7pSRXkm/p5FrTR7yom+QRbk5u83P4dr+lZi2fUS3RuHpvxzFathMe+JfQXR+i7YaTRKKsvK4SpKzB/+qqrVivdaZLfpgKrO9zyhbVnGGnrse1v61sNeu0zy3Pt/eF4EmCXxvykE09nTn4z9knf77evYLNeCuOxfSYbEn2zc3A16L510oVkarO+YLlTL+M3GIruiKIlgygkFf324NP1pQOuOUj5xCZ1IIYWCXK8pdIZ4Mrmi/3qCJE5HDPzXw96Swt4e6oUveMzw1MRs1oExBzZUZZL8M7ZIU8oAccc9YZf6bhWpRF3D3pPXRjfpJMe7bx+j0K4x6wkkUD9oY+vAjI0j8LUHYobj3aZVaKx6eihRl2JJO+Nz6/DNVBJeYe363NdBCXDbxyJbjcWkx6dDN56SqGAs+VmcizOUE2ptLdl54OVCpj2DCFJPP3e/qpareTiqO/+CqKm95s3uD7Yx2HScwkO1JU+tlE1GYx7ZuN32ILL7eWOa+EEVq2RkFNtYX6ui+6KffVq41N1GlLFlSLf8MVIZkEDpZhrVAiFXObxrhEsRbw7Q56wZzYRR3GLa5YEry8RfnrFeeyM1eqeAw5eaqoA/6nz46DzvUrhxJ/RJnxRMgHx2P10IyjgMlE8LeGx3n3cbFQKm0xyuHrgXF8ignEBS4FifA4DbiJg3n60Mo6mbL6oqUQkzGO92FQOcSH9Rq30RHM1bOSy6HIRd2s5Icb/j91/IMEfuwkzWOkGAdIAAAAASUVORK5CYII="),
        ExportMetadata("BigImageBase64", "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"),
        ExportMetadata("BackgroundColor", XTBConst.BGColor),
        ExportMetadata("PrimaryFontColor", XTBConst.PFColor),
        ExportMetadata("SecondaryFontColor", XTBConst.SFColor)]
    public class PluginDescription : PluginBase
    {
        #region Public Methods

        public override IXrmToolBoxPluginControl GetControl()
        {
            return new ShuffleBuilder();
        }

        #endregion Public Methods
    }
}