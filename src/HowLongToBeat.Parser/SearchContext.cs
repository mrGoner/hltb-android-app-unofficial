namespace HowLongToBeat.Parser;

public record SearchContext(SearchContext.Data AdditionalData, string Token, string ApiPath)
{
    public record Data(string Key, string Value);
}