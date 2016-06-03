using System;
using System.Collections.Generic;
using System.Diagnostics;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NDesk.Options;
using Mono.Collections.Generic;
using System.IO;

namespace AssemblyPlaceholder
{
    public static class Program
    {
        static void Help (OptionSet options)
        {
            Console.Error.WriteLine ("usage: Stubber.exe [-h] [-v] input... output_dir");
            Console.Error.WriteLine ("");
            Console.Error.WriteLine ("Tool to clone the public interface of a CIL assembly into a new assembly");
            Console.Error.WriteLine ("");
            options.WriteOptionDescriptions (Console.Error);
            Console.Error.WriteLine ("  input                    Path(s) to assembly(s) to clone");
            Console.Error.WriteLine ("  output_dir               Directory to write cloned assembly(s) to");
        }

        public static int Main (string[] args)
        {
            bool showHelp = false;
            bool showVersion = false;

            var options = new OptionSet { {
                    "h|help", "show this help message and exit",
                    v => showHelp = v != null
                }, {
                    "v|version", "show program's version number and exit",
                    v => showVersion = v != null
                }
            };
            List<string> positionalArgs = options.Parse (args);

            if (showHelp) {
                Help (options);
                return 0;
            }

            if (showVersion) {
                var info = FileVersionInfo.GetVersionInfo (System.Reflection.Assembly.GetEntryAssembly ().Location);
                var version = String.Format ("{0}.{1}.{2}", info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
                Console.Error.WriteLine ("AssemblyPlaceholder.exe version " + version);
                return 0;
            }

            if (positionalArgs.Count < 2) {
                Console.Error.WriteLine ("Not enough arguments");
                return 1;
            }

            string outputDir = positionalArgs [positionalArgs.Count - 1];
            for (int i = 0; i < positionalArgs.Count - 1; i++) {
                string inputPath = positionalArgs [i];
                var assembly = AssemblyDefinition.ReadAssembly (inputPath);
                notImplementedException = assembly.MainModule.Import (typeof(NotImplementedException).GetConstructor (Type.EmptyTypes));
                Strip (assembly);
                var outputPath = outputDir + new FileInfo (inputPath).Name;
                Console.WriteLine ("Writing " + outputPath);
                assembly.Write (outputPath, new WriterParameters { WriteSymbols = false });
            }
            return 0;
        }

        static MethodReference notImplementedException;

        static void Strip (AssemblyDefinition assembly)
        {
            assembly.MainModule.Types.RemoveAll (t => t.IsNotPublic);
            foreach (var type in assembly.MainModule.Types)
                Strip (type);
        }

        static void Strip (TypeDefinition type)
        {
            StripAttributes (type);

            // Fields
            type.Fields.RemoveAll (field => !field.IsPublic);
            foreach (var field in type.Fields)
                StripAttributes (field);

            // Properties
            foreach (var property in type.Properties) {
                if (property.GetMethod != null && !property.GetMethod.IsPublic)
                    property.GetMethod = null;
                if (property.SetMethod != null && !property.SetMethod.IsPublic)
                    property.SetMethod = null;
            }
            type.Properties.RemoveAll (property => (property.GetMethod == null && property.SetMethod == null));
            foreach (var property in type.Properties)
                StripAttributes (property);

            // Events
            foreach (var evnt in type.Events) {
                if (evnt.AddMethod != null && !evnt.AddMethod.IsPublic)
                    evnt.AddMethod = null;
                if (evnt.RemoveMethod != null && !evnt.RemoveMethod.IsPublic)
                    evnt.RemoveMethod = null;
            }
            type.Events.RemoveAll (evnt => (evnt.AddMethod == null && evnt.RemoveMethod == null));
            foreach (var evnt in type.Events)
                StripAttributes (evnt);

            // Methods
            type.Methods.RemoveAll (method => !method.IsPublic);
            foreach (var method in type.Methods) {
                Strip (method);
                StripAttributes (method);
            }

            // Nested types
            type.NestedTypes.RemoveAll (nestedType => !nestedType.IsNestedPublic);
            foreach (var nestedType in type.NestedTypes)
                Strip (nestedType);
        }

        static void Strip (MethodDefinition method)
        {
            method.IsInternalCall = false;
            method.Body = new MethodBody (method);
            var ilProcessor = method.Body.GetILProcessor ();
            ilProcessor.Emit (OpCodes.Newobj, notImplementedException);
            ilProcessor.Emit (OpCodes.Throw);
        }

        static void StripAttributes (ICustomAttributeProvider obj)
        {
            obj.CustomAttributes.RemoveAll (attribute => attribute.Constructor == null || !attribute.Constructor.Resolve ().IsPublic);
        }

        static Collection<T> RemoveAll<T> (this Collection<T> collection, Func<T,Boolean> predicate)
        {
            for (int i = collection.Count - 1; i >= 0; i--)
                if (predicate (collection [i]))
                    collection.RemoveAt (i);
            return collection;
        }
    }
}
