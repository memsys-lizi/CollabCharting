using System;
using System.IO;
using System.Reflection;

namespace CollabCharting
{
    internal static class ModDependencyResolver
    {
        private static string? modPath;
        private static bool installed;

        public static void Install(string path)
        {
            modPath = path;
            if (installed)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromModFolder;
            installed = true;
        }

        private static Assembly? ResolveFromModFolder(object sender, ResolveEventArgs args)
        {
            if (string.IsNullOrEmpty(modPath))
            {
                return null;
            }

            string assemblyName = new AssemblyName(args.Name).Name + ".dll";
            string candidate = Path.Combine(modPath, assemblyName);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
    }
}
