namespace CurveRisk.Copilot;

/// <summary>Prompts are files under Prompts/, embedded in the assembly so they ship and version with the code.</summary>
internal static class EmbeddedPrompt
{
    public static string Load(string fileName)
    {
        var resource = $"CurveRisk.Copilot.Prompts.{fileName}";
        using var stream = typeof(EmbeddedPrompt).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' is missing.");
        using var reader = new StreamReader(stream);

        // Normalised so the prompt bytes, and therefore the cache key, do not depend on the build machine's line endings.
        return reader.ReadToEnd().ReplaceLineEndings("\n").Trim();
    }
}
