using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using HowLongToBeat.App.Resources;
using HowLongToBeat.Parser;
using HowLongToBeat.Parser.Models.Requests;
using HowLongToBeat.Parser.Models.Responses;
using Microsoft.Extensions.Logging;
using Game = HowLongToBeat.App.Models.Game;

namespace HowLongToBeat.App.ViewModels;

public sealed class MainViewModel(
    HltbParser hltbParser,
    Func<GamesFilter, Task<GamesFilter?>> showFilterPageFunc,
    Func<DisplayAlertParams, Task<bool>> displayAlertWithCaptionFunc,
    ILogger logger)
    : INotifyPropertyChanged
{
    private static readonly DisplayAlertParams FilterChangedAlertParams = new("",
        AppResources.FilterChangedMessage, AppResources.Yes, AppResources.No);
   
    private const string SearchContextName = "SearchContextWrapperV4";
    private string _searchGameText = string.Empty;
    private GamesFilter _gameFilter = GamesFilter.Default;
    private bool _isLoading;
    private bool _isFilterPageOpened;
    private bool _isFilterModified;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsFilterModified
    {
        get => _isFilterModified;
        set => SetField(ref _isFilterModified, value);
    }

    public string SearchGameText
    {
        get => _searchGameText;
        set => SetField(ref _searchGameText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    public ICommand SearchGameCommand =>
        new Command(() => _ = SearchGames(), () =>
        {
            if (IsLoading)
                Toast.Make(AppResources.SearchInProgressMessage).Show();

            return !string.IsNullOrEmpty(SearchGameText) && !IsLoading;
        });


    public ICommand ShowFilterCommand => new Command(() =>
    {
        if (IsLoading)
            Toast.Make(AppResources.SearchInProgressMessage).Show();
        
        if(_isFilterPageOpened)
            return;
        
        _isFilterPageOpened = true;

        _ = OpenFilterPageAndGetResult().ContinueWith(_ => _isFilterPageOpened = false);
    });

    public ObservableCollection<Game> Games { get; private set; } = [];

    private async Task SearchGames()
    {
        const int RetriesCount = 3;

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            IsLoading = true;

            Games.Clear();

            var searchOptions = SearchOptions.Default with {Games = _gameFilter};
            var searchTerms = _searchGameText.Split(" ", StringSplitOptions.RemoveEmptyEntries);
            var searchRequest = new SearchRequest(SearchType.Games, searchTerms, 1, 20, searchOptions, true);

            var response = await SearchWithRetry(searchRequest, RetriesCount, cancellationTokenSource.Token);
            
            Games = new ObservableCollection<Game>(response.Games.Select(game =>
                new Game(game.Name, game.ImageUrl, game.MainTime, game.PlusExtrasTime, game.PerfectTime, game.ReleaseWorld)));

            if (Games.Count == 0)
                await Toast.Make(AppResources.NoGamesFoundMessage).Show();

            OnPropertyChanged(nameof(Games));

            logger.LogInformation("Search successful completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search games");

            await Toast.Make(AppResources.LoadFailedMessage, ToastDuration.Long).Show();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<SearchResponse> SearchWithRetry(SearchRequest searchRequest, int retryCount,
        CancellationToken cancellationToken)
    {
        var retriesLeft = retryCount;
        Exception? lastException;

        do
        {
            try
            {
                var searchContext = await GetSearchContext(retriesLeft != retryCount, cancellationToken);

                var response = await hltbParser.Search(searchRequest, searchContext, cancellationToken);

                return response;
            }
            catch (Exception ex)
            {
                lastException = ex;
                retriesLeft--;

                logger.LogWarning(ex, "Retrying search, retires left: {Retries}", retriesLeft);

                await Task.Delay(300, cancellationToken);
            }

        } while (retriesLeft > 0);

        throw lastException;
    }

    private async ValueTask<SearchContext> GetSearchContext(bool forceUpdate, CancellationToken cancellationToken)
    {
        try
        {
            var storedSearchContextWrapperRaw = Preferences.Get(SearchContextName, null);

            SearchContextWrapper searchContextWrapper;

            if (storedSearchContextWrapperRaw == null || forceUpdate)
            {
                logger.LogInformation("Search token update started. IsForce {IsForce}", forceUpdate);
                
                Preferences.Remove(SearchContextName);

                var searchContext = await hltbParser.GetSearchContext(cancellationToken) ??
                                    throw new Exception("Failed to get search context");

                searchContextWrapper = new SearchContextWrapper(searchContext, DateTimeOffset.UtcNow);

                Preferences.Set(SearchContextName, JsonSerializer.Serialize(searchContextWrapper));

                logger.LogInformation("Search token stored");
            }
            else
            {
                searchContextWrapper = JsonSerializer.Deserialize<SearchContextWrapper>(storedSearchContextWrapperRaw) ??
                                       throw new Exception("Failed to deserialize search context");

                if (DateTimeOffset.UtcNow - searchContextWrapper.StoreTime > TimeSpan.FromDays(1))
                {
                    logger.LogInformation("Search token expired. StoreDate: {StoreDate}. Start get new token",
                        searchContextWrapper.StoreTime);
                    
                    var searchContext = await hltbParser.GetSearchContext(cancellationToken) ??
                                        throw new Exception("Failed to get search context");

                    searchContextWrapper = new SearchContextWrapper(searchContext, DateTimeOffset.UtcNow);

                    Preferences.Set(SearchContextName, JsonSerializer.Serialize(searchContextWrapper));

                    logger.LogInformation("Search token stored");
                }
                
                logger.LogInformation("Search token loaded from storage");
            }
            
            return searchContextWrapper.SearchContext;
        }
        catch(Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Preferences.Remove(SearchContextName);
                logger.LogWarning(ex, "Search token not valid, remove it");
            }

            throw;
        }
    }

    private async Task OpenFilterPageAndGetResult()
    {
        var newFilter = await showFilterPageFunc(_gameFilter);
        
        if(newFilter == null)
            return;

        var isFilterChanged = !_gameFilter.Equals(newFilter);

        _gameFilter = newFilter;

        IsFilterModified = !_gameFilter.Equals(GamesFilter.Default);

        if (isFilterChanged && !string.IsNullOrEmpty(SearchGameText))
        {
            var result = await displayAlertWithCaptionFunc(FilterChangedAlertParams);

            if (result)
                _ = SearchGames();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) 
            return false;
        field = value;
        
        OnPropertyChanged(propertyName);
        return true;
    }

    private record SearchContextWrapper(SearchContext SearchContext, DateTimeOffset StoreTime);
    
}