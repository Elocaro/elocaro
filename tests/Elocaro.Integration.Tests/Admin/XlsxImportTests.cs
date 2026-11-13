using System.IO.Compression;
using System.Text;
using Elocaro.Application.Setup;
using Elocaro.Infrastructure.Setup;
using Elocaro.Integration.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elocaro.Integration.Tests.Admin;

public class XlsxImportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private InfrastructureEnvironment environment = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => environment = await InfrastructureEnvironment.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await environment.DisposeAsync();

    [Fact]
    public void Reads_strings_numbers_and_dates_of_the_first_sheet_as_csv()
    {
        var csv = Encoding.UTF8.GetString(XlsxReader.ToCsv(Workbook()));

        var lines = csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Objekt;Einheit;Mietbeginn;Kaltmiete;Lage", lines[0]);
        Assert.Equal("Lindenweg 3;Wohnung 1;01.02.2024;650,5;\"EG; links\"", lines[1]);
    }

    [Fact]
    public async Task An_excel_file_is_previewed_like_a_csv_file()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var import = scope.ServiceProvider.GetRequiredService<IDataImport>();

        var preview = await import.PreviewAsync(Workbook(), Ct);

        Assert.Empty(preview.FileErrors);
        Assert.Single(preview.Rows);
        Assert.Contains("Einheit neu", preview.Rows[0].Actions);
    }

    [Fact]
    public async Task A_broken_excel_file_gives_a_file_error()
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var import = scope.ServiceProvider.GetRequiredService<IDataImport>();

        var preview = await import.PreviewAsync([0x50, 0x4B, 0x03, 0x04, 1, 2, 3], Ct);

        Assert.Single(preview.FileErrors);
        Assert.False(preview.CanImport);
    }

    private static byte[] Workbook()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Bestand\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add(zip, "xl/styles.xml", "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cellXfs><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>");
            Add(zip, "xl/sharedStrings.xml", "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>Objekt</t></si><si><t>Einheit</t></si><si><t>Mietbeginn</t></si><si><t>Kaltmiete</t></si><si><t>Lage</t></si><si><t>Lindenweg 3</t></si><si><t>Wohnung 1</t></si></sst>");
            Add(zip, "xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
                + "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"s\"><v>2</v></c><c r=\"D1\" t=\"s\"><v>3</v></c><c r=\"E1\" t=\"s\"><v>4</v></c></row>"
                + "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>5</v></c><c r=\"B2\" t=\"s\"><v>6</v></c><c r=\"C2\" s=\"1\"><v>45323</v></c><c r=\"D2\"><v>650.5</v></c><c r=\"E2\" t=\"inlineStr\"><is><t>EG; links</t></is></c></row>"
                + "</sheetData></worksheet>");
        }

        return stream.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string xml)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }
}
