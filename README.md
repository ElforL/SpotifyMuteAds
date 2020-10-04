# SpotifyMuteAds
a C# script that mutes Spotify when an ad's playing

## running
you need to pass the [ClientID and ClientSecret](https://developer.spotify.com/documentation/web-api/quick-start/#set-up-your-account) for it to run

```
SpotifyMuteAds.exe <ClientID> <ClientSecret>
```

### packages
* [CSCore](https://github.com/filoe/cscore) to control the volume
* [SpotifyAPI-NET](https://github.com/JohnnyCrazy/SpotifyAPI-NET) for API calls
