using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using BARSBundler.Core.Helpers;
using BARSBundler.Core.Filetypes;

namespace BARSBundler.Core.Filetypes;

public struct BARSFile
{
    [StructLayout(LayoutKind.Sequential, Size = 16)]
    public struct BarsHeader
    {
        public uint Magic;
        public uint FileSize;
        public ushort Endianness;
        public byte MinorVersion;
        public byte MajorVersion;
        public uint FileCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BarsEntry
    {
        public uint BamtaOffset;
        public uint BwavOffset;
    }

    public struct BarsReserveData
    {
        public uint FileCount;
        public uint[] FileHashes;
    }

    public BarsHeader Header;
    public uint[] FileHashArray;
    public BarsEntry[] EntryArray;
    public BarsReserveData ReserveData;

    public List<AMTAFile> Metadata;
    public List<BWAVFile> Tracks;

    public BARSFile(byte[] data) => Read(new MemoryStream(data));
    public BARSFile(MemoryStream data) => Read(data);
    
    public void Read(MemoryStream data)
    {
        FileReader barsReader = new(data);

        Header = barsReader.ReadStruct<BarsHeader>();
        if (Header.MinorVersion != 2) 
            throw new InvalidDataException("Incorrect BARS file version! Only Version 1.2 is supported.");
        
        FileHashArray = new uint[Header.FileCount];

        for (int i = 0; i < Header.FileCount; i++)
            FileHashArray[i] = barsReader.ReadUInt32();

        EntryArray = new BarsEntry[Header.FileCount];

        for (int i = 0; i < Header.FileCount; i++)
            EntryArray[i] = barsReader.ReadStruct<BarsEntry>();

        ReserveData.FileCount = barsReader.ReadUInt32();
        ReserveData.FileHashes = new uint[ReserveData.FileCount];

        for (int i = 0; i < ReserveData.FileCount; i++)
            ReserveData.FileHashes[i] = barsReader.ReadUInt32();

        Metadata = [];
        Tracks = [];

        for (int i = 0; i < EntryArray.Length; i++)
        {
            barsReader.Position = EntryArray[i].BwavOffset;
            Tracks.Add(new BWAVFile(ref barsReader));
        }

        for (int i = 0; i < EntryArray.Length; i++)
        {
            barsReader.Position = EntryArray[i].BamtaOffset;
            Metadata.Add(new AMTAFile(ref barsReader));
        }
    }

    public static byte[] SoftSave(BARSFile barsData)
    {
        using MemoryStream saveStream = new();
        FileWriter barsWriter = new FileWriter(saveStream);

        // Write Header data to stream
        barsWriter.WriteStruct(barsData.Header);

        // To support adding new entries, create new file count from metadata
        var newFileCount = barsData.Metadata.Count;
        barsWriter.WriteAt(Marshal.OffsetOf<BarsHeader>("FileCount"), newFileCount); // Use AMTA file amount to calculate data (cannot be dupe)
        
        foreach (var metadata in barsData.Metadata)
            barsWriter.Write(CRC32.Compute(metadata.Path));
            //pathList.Add(CRC32.Compute(metadata.Path), metadata.Path);

        var offsetAddress = barsWriter.Position;
        long[,] offsets = new long[newFileCount, 2];

        for (int i = 0; i < newFileCount; i++)
            barsWriter.Write(0xDEADBEEFDEADBEEF); // Create a temporary area for offsets which will be filled in later

        barsWriter.Write(barsData.ReserveData.FileCount);
        foreach (uint barsHash in barsData.ReserveData.FileHashes)
            barsWriter.Write(barsHash);

        for (int a = 0; a < barsData.Metadata.Count; a++)
        {
            offsets[a, 0] = barsWriter.Position;
            barsWriter.Write(AMTAFile.Save(barsData.Metadata[a]));
            barsWriter.Align(0x4);
        }

        barsWriter.Align(0x20);

        for (int a = 0; a < barsData.Tracks.Count; a++)
        {
            offsets[a, 1] = barsWriter.Position;
            barsWriter.Write(BWAVFile.Save(barsData.Tracks[a]));
            //barsWriter.Align(0x4);
        }
        
        barsWriter.Position = offsetAddress;
        for (int l = 0; l < offsets.GetLength(0); l++)
        {
            barsWriter.Write((uint)offsets[l, 0]);
            barsWriter.Write((uint)offsets[l, 1]);
        }

        // Finally, save filesize after completing everything
        barsWriter.WriteAt(Marshal.OffsetOf<BarsHeader>("FileSize"), (uint)saveStream.Length);

        return saveStream.ToArray();    
    }   
}