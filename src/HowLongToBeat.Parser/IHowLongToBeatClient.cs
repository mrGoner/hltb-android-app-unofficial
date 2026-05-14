using HowLongToBeat.Parser.Models.Requests;
using HowLongToBeat.Parser.Models.Responses;
using Refit;

namespace HowLongToBeat.Parser;

[Headers(
    "Referer: https://howlongtobeat.com",
    "User-Agent: Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36")]
internal interface IHowLongToBeatClient
{
    [Post("/{**relativeUrl}")]
    public Task<SearchResponse> SearchGames(
        [AliasAs("relativeUrl")] string relativeUrl,
        [Header("x-auth-token")] string token,
        [Header("x-hp-key")] string tokenKey, 
        [Header("x-hp-val")] string tokenVal, 
        SearchRequest request,
        CancellationToken cancellationToken);

    [Get("/{**relativeUrl}/init?t={timestamp}")]
    public Task<ApiResponse<TokenResult>> GetAuthToken(
        [AliasAs("relativeUrl")] string relativeUrl,
        [AliasAs("timestamp")] long timestampMs,
        CancellationToken cancellationToken);
    
    [Get("/")]
    Task<string> GetMainPage(CancellationToken cancellationToken);
    
    [Get("/{**relativeUrl}")]
    Task<string> GetFileContent([AliasAs("relativeUrl")] string relativeUrl, CancellationToken cancellationToken);
}