using System.Net;
using HhAutoApplier.Core.Domain;
using HhAutoApplier.Infrastructure.Hh;
using Microsoft.Extensions.Options;

namespace HhAutoApplier.Tests;

public class HhApiClientTests
{
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"items\":[]}") });
    }

    private static HhApiClient Client(HttpStatusCode status) =>
        new(new HttpClient(new StubHandler(status)), Options.Create(new HhOptions { AccessToken = "t" }));

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedToken_ThrowsAuthError(HttpStatusCode status)
    {
        var search = new SearchProfile { Name = "dotnet", Query = "C#" };
        var e = await Assert.ThrowsAsync<HhAuthException>(() => Client(status).SearchIdsAsync(search, 0, default));
        Assert.Equal((int)status, e.Status);
        await Assert.ThrowsAsync<HhAuthException>(() => Client(status).GetAsync("1", "dotnet", default));
    }

    [Fact]
    public async Task Search_ReturnsIds_OnSuccess() =>
        Assert.Empty(await Client(HttpStatusCode.OK).SearchIdsAsync(new SearchProfile { Name = "x", Query = "x" }, 0, default));
}
