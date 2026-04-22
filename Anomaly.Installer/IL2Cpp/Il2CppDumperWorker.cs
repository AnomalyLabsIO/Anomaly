using System.Runtime.InteropServices;
using Il2CppDumper;

namespace Anomaly.Installer.IL2Cpp;

public class Il2CppDumperWorker
{
    public static void Init(byte[] il2cppBytes, string metadataPath, Config config, out Metadata metadata, out Il2Cpp il2Cpp, bool skipProtectionCheck = false)
    {
        var metadataBytes = File.ReadAllBytes(metadataPath);
        Init(il2cppBytes, metadataBytes, config, out metadata, out il2Cpp, skipProtectionCheck);
    }

    public static void Init(byte[] il2cppBytes, byte[] metadataBytes, Config config, out Metadata metadata, out Il2Cpp il2Cpp, bool skipProtectionCheck = false)
    {
        Console.WriteLine("Initializing metadata...");
        metadata = new Metadata(new MemoryStream(metadataBytes));
        Console.WriteLine($"Metadata Version: {metadata.Version}");

        Console.WriteLine("Initializing il2cpp file...");
        var il2cppMagic = BitConverter.ToUInt32(il2cppBytes, 0);
        var il2CppMemory = new MemoryStream(il2cppBytes);
        switch (il2cppMagic)
        {
            default:
                throw new NotSupportedException("ERROR: il2cpp file not supported.");
            case 0x6D736100:
                var web = new WebAssembly(il2CppMemory);
                il2Cpp = web.CreateMemory();
                break;
            case 0x304F534E:
                var nso = new NSO(il2CppMemory);
                il2Cpp = nso.UnCompress();
                break;
            case 0x905A4D: //PE
                il2Cpp = new PE(il2CppMemory);
                break;
            case 0x464c457f: //ELF
                if (il2cppBytes[4] == 2) //ELF64
                {
                    il2Cpp = new Elf64(il2CppMemory);
                }
                else
                {
                    il2Cpp = new Elf(il2CppMemory);
                }
                break;
            case 0xCAFEBABE: //FAT Mach-O
            case 0xBEBAFECA:
                var machofat = new MachoFat(new MemoryStream(il2cppBytes));
                Console.Write("Select Platform: ");
                for (var i = 0; i < machofat.fats.Length; i++)
                {
                    var fat = machofat.fats[i];
                    Console.Write(fat.magic == 0xFEEDFACF ? $"{i + 1}.64bit " : $"{i + 1}.32bit ");
                }
                Console.WriteLine();
                var key = Console.ReadKey(true);
                var index = int.Parse(key.KeyChar.ToString()) - 1;
                var magic = machofat.fats[index % 2].magic;
                il2cppBytes = machofat.GetMacho(index % 2);
                il2CppMemory = new MemoryStream(il2cppBytes);
                if (magic == 0xFEEDFACF)
                    goto case 0xFEEDFACF;
                else
                    goto case 0xFEEDFACE;
            case 0xFEEDFACF: // 64bit Mach-O
                il2Cpp = new Macho64(il2CppMemory);
                break;
            case 0xFEEDFACE: // 32bit Mach-O
                il2Cpp = new Macho(il2CppMemory);
                break;
        }
        var version = config.ForceIl2CppVersion ? config.ForceVersion : metadata.Version;
        il2Cpp.SetProperties(version, metadata.metadataUsagesCount);
        Console.WriteLine($"Il2Cpp Version: {il2Cpp.Version}");
        if (config.ForceDump || il2Cpp.CheckDump())
        {
            if (il2Cpp is ElfBase elf)
            {
                Console.WriteLine("Detected this may be a dump file.");
                Console.WriteLine("Input il2cpp dump address or input 0 to force continue:");
                var DumpAddr = Convert.ToUInt64(Console.ReadLine(), 16);
                if (DumpAddr != 0)
                {
                    il2Cpp.ImageBase = DumpAddr;
                    il2Cpp.IsDumped = true;
                    if (!config.NoRedirectedPointer)
                    {
                        elf.Reload();
                    }
                }
            }
            else
            {
                il2Cpp.IsDumped = true;
            }
        }

        Console.WriteLine("Searching...");

        var flag = il2Cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length, metadata.imageDefs.Length);

        // The original protection-detection heuristic: if PlusSearch can't locate CodeRegistration
        // on a Windows PE, assume the DLL is still Themida-wrapped. Only apply this shortcut on
        // the first (pre-unpack) attempt — after the unpacker has run, PlusSearch can still fail
        // on older Unity versions whose IL2CPP layout doesn't match the signatures, and in that
        // case we want to fall through to the Search/SymbolSearch code paths.
        if (!skipProtectionCheck
            && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && !flag
            && il2Cpp is PE)
        {
            throw new FileIsProtectedException();
        }

        if (!flag)
        {
            flag = il2Cpp.Search();
        }
        if (!flag)
        {
            flag = il2Cpp.SymbolSearch();
        }
        if (!flag)
        {
            // The upstream sample code prompts the console here. We never want an interactive
            // prompt mid-install — fail loudly so the caller can surface a proper error.
            throw new Il2CppInitException(
                "Could not locate CodeRegistration/MetadataRegistration in GameAssembly.dll. " +
                "PlusSearch, Search, and SymbolSearch all failed. " +
                "The game's IL2CPP layout may have changed, or the DLL is still packed.");
        }
        if (il2Cpp.Version >= 27 && il2Cpp.IsDumped)
        {
            var typeDef = metadata.typeDefs[0];
            var il2CppType = il2Cpp.types[typeDef.byvalTypeIndex];
            metadata.ImageBase = il2CppType.data.typeHandle - metadata.header.typeDefinitionsOffset;
        }
    }

    public static void Dump(Metadata metadata, Il2Cpp il2Cpp, string outputDir)
    {
        Console.WriteLine("Dumping...");
        var executor = new Il2CppExecutor(metadata, il2Cpp);
        Console.WriteLine("Generate struct...");
        var scriptGenerator = new StructGenerator(executor);
        scriptGenerator.WriteScript(outputDir);
        Console.WriteLine("Done!");
    }
}
