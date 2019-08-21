using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

using System.Reflection;
using System.Security.Policy;

namespace HandlingMissingTypes
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args == null || args.Length != 2)
            {
                Console.WriteLine("Incorrect number of arguments. Pass in 2 arguments where those arguments represent v1 service manifest file and v2 service manifest file respectively.");
                return;
            }

            string v1ServiceManifestPath = args[0];
            string v2ServiceManifestPath = args[1];

            if (v1ServiceManifestPath == null || v2ServiceManifestPath == null || !File.Exists(v1ServiceManifestPath) || 
                !File.Exists(v2ServiceManifestPath) || IsIncorrectExtensionSM(v2ServiceManifestPath) || IsIncorrectExtensionSM(v2ServiceManifestPath))
            {
                Console.WriteLine("Incorrect File");
                return;
            }

            bool canUpgrade = V1TypesIncludeV2Types(v1ServiceManifestPath, v2ServiceManifestPath);

            if (canUpgrade)
            {
                Console.WriteLine("Can be upgraded!");
            }
            else
            {
                Console.WriteLine("Cannot be upgraded!");
            }
        }

        private static bool IsIncorrectExtensionSM(string path)
        {
            if (!Path.GetExtension(path).Equals(".xml"))
            {
                return true;
            }

            return false;
        }

        private static bool V1TypesIncludeV2Types(string v1ServiceManifestPath, string v2ServiceManifestPath)
        {
            string version1 = GetCodePackageInfo(v1ServiceManifestPath, "Version");
            string version2 = GetCodePackageInfo(v2ServiceManifestPath, "Version");

            // if the version names  are the same, it indicates that there was no upgrade
            if (version1.Equals(version2))
            {
                return true;
            }

            HashSet<TypeReference> v2Types = GetTypesForVersion2(v2ServiceManifestPath);

            if (v2Types.Count == 0)
            {
                return true;
            }

            string v1CodePackagePath = Path.GetDirectoryName(v1ServiceManifestPath) + "\\" + GetCodePackageInfo(v1ServiceManifestPath, "Name");
            List<string> v1Files = GetFilesFromPackage(v1CodePackagePath);

            foreach (var v2Type in v2Types)
            {
                string[] v2TypeScope = v2Type.Scope.ToString().Split(',');

                // skip through primitive types based on the PublicKeyToken; .NET Core: PublicKeyToken for primtive types = b03f5f7f11d50a3a ; .NET Framework: PublicKeyToken for primtive types = b77a5c561934e089
                if (v2TypeScope.Length > 3)
                {
                    string publicKeyToken = v2TypeScope[3].ToString().Split('=')[1];

                    if (v2TypeScope[3] != null && (publicKeyToken == "b03f5f7f11d50a3a" || publicKeyToken == "b77a5c561934e089"))
                    {
                        continue;
                    }
                }

                string v2ILName = v2TypeScope[0];
                
                bool correspondingV1FileExists = false;
                foreach (var file in v1Files)
                {
                    if (file.Contains(v2ILName + ".dll") || file.Contains(v2ILName + ".exe"))
                    {
                        correspondingV1FileExists = true;

                        using (AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(file))
                        {
                            ModuleDefinition module = assemblyDefinition.MainModule;

                            bool v1TypesContainsV2Type = false;

                            foreach (var v1Type in module.Types)
                            {
                                if (v2Type.FullName.Equals(v1Type.FullName))
                                {
                                    v1TypesContainsV2Type = true;
                                    break;
                                }
                            }

                            if (!v1TypesContainsV2Type)
                            {
                                return false;
                            }
                        }
                    }
                }

                if (!correspondingV1FileExists)
                {
                    return false;
                }
            }

            return true;
        }

        private static HashSet<TypeReference> GetTypesForVersion2(string v2ServiceManifestPath)
        {
            HashSet<TypeReference> v2Types = new HashSet<TypeReference>();

            if (IsStateful(v2ServiceManifestPath))
            {
                string v2CodePackagePath = Path.GetDirectoryName(v2ServiceManifestPath) + "\\" + GetCodePackageInfo(v2ServiceManifestPath, "Name");
                List<string> ilFiles = GetFilesFromPackage(v2CodePackagePath);
                v2Types.UnionWith(GetTypesFromServiceManifestCodePackage(ilFiles));
            }

            return v2Types;
        }

        private static List<string> GetFilesFromPackage(string directoryPath)
        {
            List<string> files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".dll") || f.EndsWith(".exe")).ToList();

            return files;
        }

        private static HashSet<TypeReference> GetTypesFromServiceManifestCodePackage(List<string> files)
        {
            HashSet<TypeReference> v2Types = new HashSet<TypeReference>();

            var readerParameters = new ReaderParameters
            {
                AssemblyResolver = new DefaultAssemblyResolver(),
            };

            foreach (var file in files)
            {
                try
                {
                    using (AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(file, readerParameters))
                    {
                        ModuleDefinition module = assemblyDefinition.MainModule;
                        if (module.HasTypeReference("Microsoft.ServiceFabric.Data.IReliableStateManager"))
                        {
                            foreach (var type in module.GetTypes())
                            {
                                if (type.HasMethods)
                                {
                                    foreach (var method in type.Methods)
                                    {
                                        if (method.HasBody && method.Body.Instructions != null)
                                        {
                                            foreach (var v2Type in GetTypesInMethod(method))
                                            {
                                                v2Types.UnionWith(GetTypesInMethod(method));
                                            }

                                        }
                                    }
                                }
                            }
                        }
                    }

                }
                catch (BadImageFormatException)
                {

                }
            }

            return v2Types;
        }

        private static HashSet<TypeReference> GetTypesInMethod(MethodDefinition method)
        {
            HashSet<TypeReference> v2Types = new HashSet<TypeReference>();
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Calli || instruction.OpCode == OpCodes.Callvirt)
                {
                    MethodReference methodCall = instruction.Operand as MethodReference;
                    if (methodCall?.DeclaringType?.FullName == "Microsoft.ServiceFabric.Data.IReliableStateManager" &&
                        methodCall.CallingConvention == MethodCallingConvention.Generic && methodCall.FullName.Contains("GetOrAddAsync"))
                    {
                        GenericInstanceMethod genericMethodCall = (GenericInstanceMethod)methodCall;
                        if (genericMethodCall.HasGenericArguments)
                        {
                            var arguments = genericMethodCall.GenericArguments[0];
                            if (arguments.IsGenericInstance)
                            {
                                GenericInstanceType genericInstanceType = (GenericInstanceType)arguments;
                                if (genericInstanceType.HasGenericArguments)
                                {
                                    foreach (var genericType in genericInstanceType.GenericArguments)
                                    {
                                        if (!genericType.IsGenericParameter && genericType.FullName != null)
                                        {
                                            v2Types.Add(genericType);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return v2Types;
        }

        private static bool IsStateful(string path)
        {
            using (StreamReader sr = File.OpenText(path))
            {
                string s = "";
                while ((s = sr.ReadLine()) != null)
                {
                    if (s.Contains("Stateful"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string GetCodePackageInfo(string path, string infoType)
        {
            string infoValue = String.Empty;

            using (StreamReader sr = File.OpenText(path))
            {
                string s = String.Empty;
                while ((s = sr.ReadLine()) != null)
                {
                    if (s.Contains("CodePackage"))
                    {
                        string[] codePackageInfo = s.Split(' ');

                        foreach (var info in codePackageInfo)
                        {
                            if (info.Contains(infoType))
                            {
                                infoValue = info.Split('=')[1];
                            }
                        }
                    }
                }
            }

            infoValue = infoValue.Trim('>').Trim('\"');

            return infoValue;
        }
    }
}
