namespace Shop.Reporting;

public sealed class ReportExporter(string exportRoot)
{
    public async Task<Stream> OpenAsync(string reportName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reportName))
        {
            throw new ArgumentException("A report name is required.", nameof(reportName));
        }

        var path = Path.Combine(exportRoot, reportName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("No such report.", reportName);
        }

        var buffer = new MemoryStream();
        await using (var file = File.OpenRead(path))
        {
            await file.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;
        return buffer;
    }
}
