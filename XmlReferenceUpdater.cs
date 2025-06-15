namespace PPTcrunch;

public static class XmlReferenceUpdater
{
    public static void UpdateXmlReferences(string workingDir, Dictionary<string, string> compressedVideos)
    {
        // Find all XML files in the PPTX structure
        var xmlFiles = new List<string>();

        // Common locations for XML files that might reference media
        string[] searchDirs = {
            Path.Combine(workingDir, "ppt", "slides"),
            Path.Combine(workingDir, "ppt", "slideLayouts"),
            Path.Combine(workingDir, "ppt", "slideMasters"),
            Path.Combine(workingDir, "ppt"),
            workingDir
        };

        foreach (string dir in searchDirs.Where(Directory.Exists))
        {
            xmlFiles.AddRange(Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories));
            xmlFiles.AddRange(Directory.GetFiles(dir, "*.rels", SearchOption.AllDirectories));
        }

        Console.WriteLine($"Found {xmlFiles.Count} XML/RELS files to check for media references");

        int updatedFiles = 0;
        foreach (string xmlFile in xmlFiles)
        {
            try
            {
                if (UpdateXmlFile(xmlFile, compressedVideos))
                {
                    updatedFiles++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Could not update XML file {Path.GetFileName(xmlFile)}: {ex.Message}");
            }
        }

        Console.WriteLine($"Updated references in {updatedFiles} XML files");
    }

    private static bool UpdateXmlFile(string xmlFilePath, Dictionary<string, string> compressedVideos)
    {
        string content = File.ReadAllText(xmlFilePath);
        string originalContent = content;
        bool modified = false;

        foreach (var kvp in compressedVideos)
        {
            string originalFileName = kvp.Key;
            string newFileName = kvp.Value;

            if (originalFileName != newFileName)
            {
                // Look for various ways the filename might be referenced
                string[] patterns = {
                    $"media/{originalFileName}",
                    $"../media/{originalFileName}",
                    $"ppt/media/{originalFileName}",
                    originalFileName
                };

                foreach (string pattern in patterns)
                {
                    string newPattern = pattern.Replace(originalFileName, newFileName);
                    if (content.Contains(pattern) && pattern != newPattern)
                    {
                        content = content.Replace(pattern, newPattern);
                        modified = true;
                        Console.WriteLine($"  Updated reference in {Path.GetFileName(xmlFilePath)}: {pattern} -> {newPattern}");
                    }
                }
            }
        }

        if (modified)
        {
            File.WriteAllText(xmlFilePath, content);
            return true;
        }

        return false;
    }
}