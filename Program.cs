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
            Console.Error.WriteLine ("usage: stubber [-h] [-v] input... output_dir");
            Console.Error.WriteLine ("");
            Console.Error.WriteLine ("Tool to clone the public interface of a CIL assembly into a new assembly");
            Console.Error.WriteLine ("");
            options.WriteOptionDescriptions (Console.Error);
            Console.Error.WriteLine ("  input                    Path(s) to assembly(s) to clone");
            Console.Error.WriteLine ("  output_dir               Directory to write cloned assembly(s) to");
        }

        public static Int32 Main (String[] args)
        {
            Boolean showHelp = false;
            Boolean showVersion = false;

            OptionSet options = new OptionSet { {
                    "h|help", "show this help message and exit",
                    v => showHelp = v != null
                }, {
                    "v|version", "show program's version number and exit",
                    v => showVersion = v != null
                }
            };
            List<String> positionalArgs = options.Parse (args);

            if (showHelp) {
                Help (options);
                return 0;
            }

            if (showVersion) {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo (System.Reflection.Assembly.GetEntryAssembly ().Location);
                String version = $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
                Console.Error.WriteLine ("Stubber version " + version);
                return 0;
            }

            if (positionalArgs.Count < 2) {
                Console.Error.WriteLine ("Not enough arguments");
                return 1;
            }

            String outputDir = positionalArgs [positionalArgs.Count - 1];
            for (Int32 i = 0; i < positionalArgs.Count - 1; i++) {
                String inputPath = positionalArgs [i];
                AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly (inputPath);
                _notImplementedException = assembly.MainModule.Import (typeof(NotImplementedException).GetConstructor (Type.EmptyTypes));
                Strip (assembly);
                String outputPath = outputDir + new FileInfo (inputPath).Name;
                Console.WriteLine ("Writing " + outputPath);
                assembly.Write (outputPath, new WriterParameters { WriteSymbols = false });
            }
            return 0;
        }

        static MethodReference _notImplementedException;

        static void Strip (AssemblyDefinition assembly)
        {
            StripAttributes(assembly);
            foreach (TypeDefinition type in assembly.MainModule.Types)
                Strip (type);
            assembly.MainModule.Types.RemoveAll (t => t.IsNotPublic);
            assembly.MainModule.AssemblyReferences.RemoveAll(a => a.Name == "mscorlib" && a.Version.Major == 4);
        }

        static void Strip (TypeDefinition type)
        {
            StripAttributes (type);

            // Fields
            type.Fields.RemoveAll (field => !(field.IsPublic || field.IsFamily));
            foreach (FieldDefinition field in type.Fields)
                StripAttributes (field);

            // Properties
            foreach (PropertyDefinition property in type.Properties) {
                if (property.GetMethod != null && !(property.GetMethod.IsPublic || property.GetMethod.IsFamily) )
                    property.GetMethod = null;
                if (property.SetMethod != null && !(property.SetMethod.IsPublic || property.SetMethod.IsFamily))
                    property.SetMethod = null;
            }
            type.Properties.RemoveAll (property => (property.GetMethod == null && property.SetMethod == null));
            foreach (PropertyDefinition property in type.Properties)
                StripAttributes (property);

            // Events
            foreach (EventDefinition evnt in type.Events) {
                if (evnt.AddMethod != null && !(evnt.AddMethod.IsPublic || evnt.AddMethod.IsFamily))
                    evnt.AddMethod = null;
                if (evnt.RemoveMethod != null && !(evnt.RemoveMethod.IsPublic || evnt.RemoveMethod.IsFamily))
                    evnt.RemoveMethod = null;
            }
            type.Events.RemoveAll (e => (e.AddMethod == null && e.RemoveMethod == null));
            foreach (EventDefinition e in type.Events)
                StripAttributes (e);

            // Methods
            type.Methods.RemoveAll (method => !(method.IsPublic || method.IsFamily));
            foreach (MethodDefinition method in type.Methods) {
                Strip (method);
                StripAttributes (method);
            }

            // Nested types
            type.NestedTypes.RemoveAll (nestedType => !nestedType.IsNestedPublic);
            foreach (TypeDefinition nestedType in type.NestedTypes)
                Strip (nestedType);
        }

        static void Strip (MethodDefinition method)
        {
            method.IsInternalCall = false;
            method.Body = new MethodBody (method);
            ILProcessor ilProcessor = method.Body.GetILProcessor ();
            ilProcessor.Emit (OpCodes.Newobj, _notImplementedException);
            ilProcessor.Emit (OpCodes.Throw);
        }

        static void StripAttributes (ICustomAttributeProvider obj)
        {
            obj.CustomAttributes.RemoveAll(attribute =>
                attribute.Constructor == null || !(attribute.Constructor.Resolve().IsPublic || attribute.Constructor.Resolve().IsFamily) ||
                attribute.AttributeType.Resolve().IsNotPublic);
        }

        static Collection<T> RemoveAll<T> (this Collection<T> collection, Func<T,Boolean> predicate)
        {
            for (Int32 i = collection.Count - 1; i >= 0; i--)
                if (predicate(collection[i]))
                {
                    collection.RemoveAt(i);
                }

            return collection;
        }
    }
}
