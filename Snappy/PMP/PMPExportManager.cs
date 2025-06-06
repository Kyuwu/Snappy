using Snapper.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Snapper.Utils;
using System.Text.Json;
using System.ComponentModel.Design;
using System.IO.Compression;

namespace Snapper.PMP
{
    public class PMPExportManager
    {
        private Plugin plugin;
        public PMPExportManager(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public void SnapshotToPMP(string snapshotPath)
        {
            Logger.Debug($"Operating on {snapshotPath}");

            string snapshotFile = Path.Combine(snapshotPath, "snapshot.json");

            if (!File.Exists(snapshotFile))
            {
                Logger.Warn($"Snapshot json not found at: {snapshotFile}, aborting.");
                return;
            }

            string infoJson;
            try
            {
                infoJson = File.ReadAllText(snapshotFile);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to read snapshot.json: {ex.Message}");
                return;
            }

            SnapshotInfo? snapshotInfo;
            try
            {
                snapshotInfo = JsonSerializer.Deserialize<SnapshotInfo>(infoJson);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to deserialize snapshot.json: {ex.Message}");
                return;
            }

            if (snapshotInfo == null)
            {
                Logger.Warn("Deserialized snapshotInfo was null, aborting.");
                return;
            }

            //begin building PMP
            string snapshotName = new DirectoryInfo(snapshotPath).Name;
            string pmpFileName = $"{snapshotName}_{Guid.NewGuid()}";


            string workingDirectory = Path.Combine(plugin.Configuration.WorkingDirectory, $"temp_{pmpFileName}");
            if (!Directory.Exists(workingDirectory))
            {
                Directory.CreateDirectory(workingDirectory);
            }

            //meta.json
            PMPMetadata metadata = new PMPMetadata();
            metadata.Name = snapshotName;
            metadata.Author = $"SnapperFork";
            using(FileStream stream = new FileStream(Path.Combine(workingDirectory, "meta.json"), FileMode.Create))
            {
                JsonSerializer.Serialize(stream, metadata);
            }

            //default_mod.json
            PMPDefaultMod defaultMod = new PMPDefaultMod();
            foreach (var file in snapshotInfo.FileReplacements)
            {
                foreach(var replacement in file.Value)
                {
                    defaultMod.Files.Add(replacement, file.Key);
                }
            }

            List<PMPManipulationEntry>? manipulations;
            FromCompressedBase64<List<PMPManipulationEntry>>(snapshotInfo.ManipulationString, out manipulations);
            if(manipulations != null)
            {
                defaultMod.Manipulations = manipulations;
            }
            using (FileStream stream = new FileStream(Path.Combine(workingDirectory, "default_mod.json"), FileMode.Create))
            {
                JsonSerializer.Serialize(stream, defaultMod, new JsonSerializerOptions { WriteIndented = true});
            }

            //mods
            foreach(var file in snapshotInfo.FileReplacements)
            {

                string modPath = Path.Combine(snapshotPath, file.Key);
                string destPath = Path.Combine(workingDirectory, file.Key);
                Logger.Debug($"Copying {modPath}");
                Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? "");
                File.Copy(modPath, destPath);
            }

            //zip and make pmp file
            ZipFile.CreateFromDirectory(workingDirectory, Path.Combine(plugin.Configuration.WorkingDirectory, $"{pmpFileName}.pmp"));

            //cleanup
            Directory.Delete(workingDirectory, true);
        }


        // Decompress a base64 encoded string to the given type and a prepended version byte if possible.
        // On failure, data will be default and version will be byte.MaxValue.
        internal static byte FromCompressedBase64<T>(string base64, out T? data)
        {
            var version = byte.MaxValue;
            try
            {
                var bytes = Convert.FromBase64String(base64);
                using var compressedStream = new MemoryStream(bytes);
                using var zipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
                using var resultStream = new MemoryStream();
                zipStream.CopyTo(resultStream);
                bytes = resultStream.ToArray();
                version = bytes[0];
                var json = Encoding.UTF8.GetString(bytes, 1, bytes.Length - 1);
                data = JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                data = default;
            }

            return version;
        }
    }
}
