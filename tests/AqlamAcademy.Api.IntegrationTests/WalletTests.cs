using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static AqlamAcademy.Api.IntegrationTests.CourseTestHelpers;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class WalletTests(ApiFactory factory)
{
    [Fact]
    public async Task Earnings_are_pending_then_available_and_a_withdrawal_is_reserved_paid_or_returned()
    {
        var token = $"zw{Guid.NewGuid():N}"[..10];
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "wal");
        var courseId = await MarketplaceTests.PublicCourseAsync(teacher, org, $"Wallet {token}", price: 200m);
        var admin = await AdminAsync();
        (await admin.PutAsJsonAsync($"/api/platform/marketplace/organizations/{org}/commission", new { commissionPercent = 20m })).EnsureSuccessStatusCode();

        var student = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, "walbuy");
        var paymentId = (await Data(await student.PostAsync($"/api/orgs/{org}/learn/courses/{courseId}/checkout", null))).GetProperty("paymentId").GetString();
        (await student.PostAsJsonAsync($"/api/payments/{paymentId}/test-complete", new { succeed = true })).EnsureSuccessStatusCode();

        var originalFees = await Data(await admin.GetAsync("/api/platform/marketplace/fees"));
        try
        {
            // Inside the hold period the sale is pending and nothing can be withdrawn.
            await SetFeesAsync(admin, originalFees, holdDays: 7, minimumSar: null);
            var wallet = await Data(await teacher.GetAsync($"/api/orgs/{org}/wallet"));
            Assert.Equal(160m, wallet.GetProperty("total").GetDecimal());
            Assert.Equal(160m, wallet.GetProperty("pending").GetDecimal());
            Assert.Equal(0m, wallet.GetProperty("available").GetDecimal());
            await SaveAccountAsync(teacher, org);
            Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PostAsJsonAsync($"/api/orgs/{org}/wallet/payouts", new { amount = 50m })).StatusCode);

            // After the hold, it is available; limits are enforced.
            await SetFeesAsync(admin, originalFees, holdDays: 0, minimumSar: 20m);
            wallet = await Data(await teacher.GetAsync($"/api/orgs/{org}/wallet"));
            Assert.Equal(160m, wallet.GetProperty("available").GetDecimal());
            Assert.Equal(20m, wallet.GetProperty("minimumPayout").GetDecimal());
            Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PostAsJsonAsync($"/api/orgs/{org}/wallet/payouts", new { amount = 10m })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PostAsJsonAsync($"/api/orgs/{org}/wallet/payouts", new { amount = 500m })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PostAsJsonAsync($"/api/orgs/{org}/wallet/payouts", new { amount = 10.005m })).StatusCode);

            // A request reserves the money; only one can be open.
            var first = await Data(await teacher.PostAsJsonAsync($"/api/orgs/{org}/wallet/payouts", new { amount = 100m }));
            Assert.Equal("Requested", first.GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await teacher.PostAsJsonAsync($"/api/orgs/{org}/wallet/payouts", new { amount = 30m })).StatusCode);
            wallet = await Data(await teacher.GetAsync($"/api/orgs/{org}/wallet"));
            Assert.Equal(60m, wallet.GetProperty("available").GetDecimal());
            Assert.Equal(100m, wallet.GetProperty("inTransit").GetDecimal());
            Assert.Equal(160m, wallet.GetProperty("total").GetDecimal());

            // Cancelling gives the money back.
            var firstId = first.GetProperty("id").GetString();
            Assert.Equal("Cancelled", (await Data(await teacher.PostAsync($"/api/orgs/{org}/wallet/payouts/{firstId}/cancel", null))).GetProperty("status").GetString());
            Assert.Equal(160m, (await Data(await teacher.GetAsync($"/api/orgs/{org}/wallet"))).GetProperty("available").GetDecimal());

            // The platform admin pays: needs a transfer reference; teachers and strangers cannot.
            var second = await Data(await teacher.PostAsJsonAsync($"/api/orgs/{org}/wallet/payouts", new { amount = 100m }));
            var secondId = second.GetProperty("id").GetString();
            Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/platform/payouts")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/platform/payouts/{secondId}/paid", new { transferReference = "X" })).StatusCode);
            var queue = await Data(await admin.GetAsync("/api/platform/payouts?status=Requested"));
            Assert.Contains(queue.EnumerateArray(), p => p.GetProperty("payout").GetProperty("id").GetString() == secondId && p.GetProperty("organizationSlug").GetString() == org);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/platform/payouts/{secondId}/paid", new { transferReference = " " })).StatusCode);
            var paid = await Data(await admin.PostAsJsonAsync($"/api/platform/payouts/{secondId}/paid", new { transferReference = "BANK-123" }));
            Assert.Equal("Paid", paid.GetProperty("payout").GetProperty("status").GetString());

            // Settled once: neither a second payment, a rejection nor a cancellation changes it.
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/platform/payouts/{secondId}/paid", new { transferReference = "BANK-999" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/platform/payouts/{secondId}/reject", new { reason = "late" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await teacher.PostAsync($"/api/orgs/{org}/wallet/payouts/{secondId}/cancel", null)).StatusCode);
            wallet = await Data(await teacher.GetAsync($"/api/orgs/{org}/wallet"));
            Assert.Equal(60m, wallet.GetProperty("total").GetDecimal());
            Assert.Equal(0m, wallet.GetProperty("inTransit").GetDecimal());

            // A rejected request returns the money and tells the teacher why.
            var third = await Data(await teacher.PostAsJsonAsync($"/api/orgs/{org}/wallet/payouts", new { amount = 60m }));
            var thirdId = third.GetProperty("id").GetString();
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/platform/payouts/{thirdId}/reject", new { reason = "" })).StatusCode);
            (await admin.PostAsJsonAsync($"/api/platform/payouts/{thirdId}/reject", new { reason = "IBAN is wrong" })).EnsureSuccessStatusCode();
            wallet = await Data(await teacher.GetAsync($"/api/orgs/{org}/wallet"));
            Assert.Equal(60m, wallet.GetProperty("available").GetDecimal());
            Assert.Contains(wallet.GetProperty("history").EnumerateArray(), p => p.GetProperty("rejectionReason").GetString() == "IBAN is wrong");

            // The platform side never loses or invents money: earnings still match the one sale.
            var earnings = await Data(await teacher.GetAsync($"/api/orgs/{org}/earnings"));
            Assert.Equal(160m, Assert.Single(earnings.GetProperty("totals").EnumerateArray()).GetProperty("net").GetDecimal());
        }
        finally
        {
            await SetFeesAsync(admin, originalFees, originalFees.GetProperty("payoutHoldDays").GetInt32(), originalFees.TryGetProperty("minimumPayoutSar", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetDecimal() : null);
        }
    }

    [Fact]
    public async Task Bank_details_are_checked_and_only_owners_may_withdraw()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "walb");
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PutAsJsonAsync($"/api/orgs/{org}/wallet/account", new { accountHolder = "", bankName = "NCB", iban = "SA03" })).StatusCode);
        var saved = await Data(await teacher.PutAsJsonAsync($"/api/orgs/{org}/wallet/account",
            new { accountHolder = "Huda Ali", bankName = "NCB", iban = "sa03 8000 0000 6080 1016 7519" }));
        Assert.Equal("SA0380000000608010167519", saved.GetProperty("iban").GetString());

        // No bank details, no request: a brand-new teacher with no balance is refused politely.
        var (other, otherOrg) = await TeacherWithPersonalOrgAsync(factory, "walc");
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync($"/api/orgs/{otherOrg}/wallet/payouts", new { amount = 10m })).StatusCode);

        // Another organization's wallet is not reachable.
        Assert.False((await other.GetAsync($"/api/orgs/{org}/wallet")).IsSuccessStatusCode);
        var student = factory.CreateBrowserClient();
        await TestUsers.RegisterAndLoginAsync(student, TestUsers.Student, "walstu");
        Assert.False((await student.PostAsJsonAsync($"/api/orgs/{org}/wallet/payouts", new { amount = 10m })).IsSuccessStatusCode);
    }

    private static async Task SaveAccountAsync(HttpClient teacher, string org) =>
        (await teacher.PutAsJsonAsync($"/api/orgs/{org}/wallet/account", new { accountHolder = "Huda Ali", bankName = "NCB", iban = "SA0380000000608010167519" })).EnsureSuccessStatusCode();

    private static async Task SetFeesAsync(HttpClient admin, JsonElement current, int holdDays, decimal? minimumSar)
    {
        decimal? Num(string name) => current.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
        (await admin.PutAsJsonAsync("/api/platform/marketplace/fees", new
        {
            commissionPercent = Num("commissionPercent"),
            monthlyStudentFeeSar = Num("monthlyStudentFeeSar"),
            monthlyStudentFeeEgp = Num("monthlyStudentFeeEgp"),
            minimumPayingAge = current.GetProperty("minimumPayingAge").GetInt32(),
            payoutHoldDays = holdDays,
            minimumPayoutSar = minimumSar,
            minimumPayoutEgp = Num("minimumPayoutEgp")
        })).EnsureSuccessStatusCode();
    }

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        (await admin.PostAsJsonAsync("/api/Auth/login", new { userName = ApiFactory.PlatformAdminUserName, password = ApiFactory.PlatformAdminPassword })).EnsureSuccessStatusCode();
        return admin;
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
