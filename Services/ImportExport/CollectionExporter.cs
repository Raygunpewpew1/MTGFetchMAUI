using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AetherVault.Data;
using CsvHelper;
using CsvHelper.Configuration;

namespace AetherVault.Services.ImportExport;

public class CollectionExporter
{
    private readonly ICollectionRepository _collectionRepo;

    public CollectionExporter(ICollectionRepository collectionRepo)
    {
        _collectionRepo = collectionRepo;
    }

    // Export using Moxfield format as it's a very standard/widely accepted format
    // Count,Tradelist Count,Name,Edition,Condition,Language,Foil,Tags,Last Modified,Collector Number,Alter,Proxy,Purchase Price
    public async Task<string> ExportToCsvAsync()
    {
        var items = await _collectionRepo.GetCollectionAsync();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };

        using var stringWriter = new StringWriter();
        using var csv = new CsvWriter(stringWriter, config);

        // Write Headers
        csv.WriteField("Count");
        csv.WriteField("Tradelist Count");
        csv.WriteField("Name");
        csv.WriteField("Edition");
        csv.WriteField("Condition");
        csv.WriteField("Language");
        csv.WriteField("Foil");
        csv.WriteField("Tags");
        csv.WriteField("Last Modified");
        csv.WriteField("Collector Number");
        csv.WriteField("Alter");
        csv.WriteField("Proxy");
        csv.WriteField("Purchase Price");
        await csv.NextRecordAsync();

        foreach (var item in items)
        {
            if (item.Card == null) continue;

            csv.WriteField(item.Quantity);
            csv.WriteField(0);
            csv.WriteField(item.Card.Name);
            csv.WriteField(item.Card.SetCode);
            csv.WriteField("Near Mint");
            csv.WriteField("English");
            csv.WriteField(item.IsEtched ? "etched" : (item.IsFoil ? "foil" : ""));
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField(item.Card.Number ?? "");
            csv.WriteField("False");
            csv.WriteField("False");
            csv.WriteField("");
            await csv.NextRecordAsync();
        }

        return stringWriter.ToString();
    }
}
