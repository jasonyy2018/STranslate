using System.Reflection;
using System.Runtime.InteropServices;

#if DEBUG

[assembly: AssemblyConfiguration("Debug")]
[assembly: AssemblyDescription("Debug build, https://github.com/ZGGSONG/STranslate")]
#else
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("Release build, https://github.com/ZGGSONG/STranslate")]
#endif

[assembly: AssemblyCompany("STranslate")]
[assembly: AssemblyProduct("STranslate")]
[assembly: AssemblyCopyright("The MIT License (MIT)")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.0.0")]
[assembly: AssemblyFileVersion("1.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
