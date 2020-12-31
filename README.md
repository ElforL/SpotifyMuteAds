# SpotifyMuteAds
a windows C# script that mutes Spotify when an ad's playing

## [Download](https://github.com/ElforL/SpotifyMuteAds/releases)

## Running
### Setting up:
you'll need a [ClientID and ClientSecret](https://developer.spotify.com/documentation/web-api/quick-start/#set-up-your-account) for it to run.
when you run for the first time it it'll ask you to enter them in the browser, then will ask you to authorize the app in your spotify account.

you can pass the ClientID and ClientSecret as paramaters to avoid entering them in the browser  
cmd/shell command:
```
SpotifyMuteAds.exe <ClientID> <ClientSecret>
```

you can use `_RUN.bat` file, **but you need to edit it first**
1. right-click on it and press 'Edit'
2. now replace the \<ClientID\> and \<ClientSecret\> with your ClientID and ClientSecret and save the file
3. now you can use `_RUN.bat` to run the script

### Starting:
After running for the first time, a browser tab will open  
  1. If you didn't pass the clientID and clientSecret it'll ask you to enter them now. if you did, then you can skip this step
  2. Then it's goning to ask you to authourize the app with your spotify account, do it and wait for the message ("OK - This window can be closed now")

Great! we're done.  
The console will display the current state as:
* **Playing: \<current song\>**: the currently playing song
* **Paused**: when the user isn't playing anything
* **Muted**: when an ad is running and it's muted

## Packages
* [CSCore](https://github.com/filoe/cscore) to control the volume
* [SpotifyAPI-NET](https://github.com/JohnnyCrazy/SpotifyAPI-NET) for API calls
