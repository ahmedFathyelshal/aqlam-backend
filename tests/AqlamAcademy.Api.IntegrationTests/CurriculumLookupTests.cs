using System.Net.Http.Json;
using System.Text.Json;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CurriculumLookupTests(ApiFactory factory)
{
    [Fact]
    public async Task Saudi_curricula_include_national_and_international_but_not_egyptian()
    {
        var client = factory.CreateClient();

        var codes = await GetCodesAsync(client, "/api/Lookups/curricula?countryCode=SA");

        Assert.Contains("SA_NATIONAL", codes);
        Assert.Contains("SA_ADMISSION_TESTS", codes);
        Assert.Contains("AMERICAN", codes);
        Assert.Contains("BRITISH", codes);
        Assert.DoesNotContain("EG_NATIONAL", codes);
        Assert.DoesNotContain("EG_AZHAR", codes);
        Assert.Equal("SA_NATIONAL", codes[0]); // national curricula are listed first
    }

    [Fact]
    public async Task Egyptian_curricula_include_azhar_and_not_saudi()
    {
        var client = factory.CreateClient();

        var codes = await GetCodesAsync(client, "/api/Lookups/curricula?countryCode=EG");

        Assert.Contains("EG_NATIONAL", codes);
        Assert.Contains("EG_AZHAR", codes);
        Assert.DoesNotContain("SA_NATIONAL", codes);
    }

    [Fact]
    public async Task Stages_are_filtered_by_curriculum()
    {
        var client = factory.CreateClient();
        var curricula = (await client.GetFromJsonAsync<JsonElement>("/api/Lookups/curricula?countryCode=SA")).GetProperty("data");
        var saudiId = curricula.EnumerateArray().First(c => c.GetProperty("code").GetString() == "SA_NATIONAL").GetProperty("id").GetInt32();

        var stages = (await client.GetFromJsonAsync<JsonElement>($"/api/Lookups/education-stages?curriculumId={saudiId}")).GetProperty("data");

        var names = stages.EnumerateArray().Select(s => s.GetProperty("nameEn").GetString()).ToList();
        Assert.Equal(["Kindergarten", "Primary", "Intermediate", "Secondary"], names);
        Assert.All(stages.EnumerateArray(), s => Assert.Equal(saudiId, s.GetProperty("curriculumId").GetInt32()));
    }

    private static async Task<List<string>> GetCodesAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("data")
            .EnumerateArray().Select(c => c.GetProperty("code").GetString()!).ToList();
}
