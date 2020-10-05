using System;
using System.IO;

using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using SpotifyAPI.Web.Models;

using CSCore.CoreAudioAPI;
using MMDeviceEnumerator = CSCore.CoreAudioAPI.MMDeviceEnumerator;

using System.Threading;
using System.Threading.Tasks;

class Program
{
    
    static AudioSessionControl Spotifysession = null;

    const int TIMEOUT_SECONDS = 60;
    static String clientID;
    static String clientSecret;
    const String redirectURL = "http://localhost:4002";
    static SpotifyWebAPI spotifyAPI;
    static AuthorizationCodeAuth auth;
    static Token token;

    public static void Main(string[] args)
    {
        if(args.Length != 2)
        {
            Console.WriteLine("No/invalid paramaters detected");
            Console.WriteLine("When running, pass the clientID and the clientSecret as a paramaters to avoid entering them in the browser everytime");
            Console.WriteLine("cmd command: SpotifyMuteAds.exe <clientID> <clientSecret>");
            Console.WriteLine("You can use _RUN.bat file but you need to edit it first");
            Console.WriteLine("");
        }
        else
        {
            clientID = args[0];
            clientSecret = args[1];
        }
        
        // async Main()
        MainAsync(args).GetAwaiter().GetResult();
    }

    static async Task MainAsync(string[] args)
    {
        // Finding Spotify audio session
        FindAudioSession();

        //getting access token
        CreateSpotifyAPI();

        await AppLoop();
    }

    static async Task AppLoop()
    {
        String status = "";
        int errorsCount = 0;
        bool pauseMode = false;
        DateTime startTime = DateTime.Now;
        int sleepTime = 5000; // default sleep duration = 5s

        PlaybackContext current;

        

        while (true)
        {

            if (spotifyAPI != null)
            {
                
                if (!pauseMode)
                {
                    if (token.IsExpired()) await RefreshToken();
                    current = spotifyAPI.GetPlayingTrack();

                    if (current.HasError())
                    { // Error; didn't get the track
                        errorsCount++;
                        Console.WriteLine("Error Status: " + current.Error.Status);
                        Console.WriteLine("Error Msg: " + current.Error.Message);
                        sleepTime = 10000;

                        if (current.Error.Status == 429)
                        {
                            Console.WriteLine("`current header` key = `Retry-After` : " + current.Header("Retry-After"));
                        }
                        if(errorsCount > 5)
                        {
                            Console.WriteLine("BREAKDOWN: " + errorsCount + " errors in a row");
                            Console.WriteLine("Shuting down...");
                            Environment.Exit(1);
                        }
                    }
                    else
                    { // Got the track
                        errorsCount = 0;
                        if (current.IsPlaying)
                        {
                            if (current.CurrentlyPlayingType == TrackType.Ad)
                            {
                                status = "Muted";
                                SetMute(true);
                                sleepTime = 1500;
                            }
                            else
                            {
                                status = current.Item.Name;
                                SetMute(false);

                                // set the sleep time to the duration divided by 3 if it's less than the remiaing time
                                if (current.Item.DurationMs - current.ProgressMs < current.Item.DurationMs / 3)
                                    sleepTime = current.Item.DurationMs - current.ProgressMs;
                                else
                                    sleepTime = current.Item.DurationMs / 3;
                            }
                        }
                        else
                        {
                            pauseMode = true;
                            continue;
                        }
                    }
                }
                else
                { // pauseMode == true
                    SetMute(false);
                    SetVolume(1);
                    status = "Paused";
                    sleepTime = 1000;
                    if(Spotifysession.QueryInterface<AudioMeterInformation>().PeakValue > 0)
                    { // if spotify's volume is more than 0 (playing someting) then set pauseMode to false
                        pauseMode = false;
                        continue;
                    }
                }
            }
            else // when SpotifyAPI is null, i.e didn't recive the access token yet
            {
                // shutdown if connecting took more than TIMEOUT_SECONDS
                if ((DateTime.Now - startTime).TotalSeconds > TIMEOUT_SECONDS)
                {
                    Console.WriteLine(String.Format("Timeout: connecting took too long: {0:0.00} seconds", (DateTime.Now - startTime).TotalSeconds));
                    Console.WriteLine("Shutting down...");
                    Environment.Exit(1);
                }

            }

            Console.Write(String.Format("\r{0,-50}",status));
            Thread.Sleep(sleepTime);
        }
    }

    static void FindAudioSession()
    {
        Console.WriteLine("Finding Spotify audio session...");
        var sessionManager = GetDefaultAudioSessionManager2(DataFlow.Render);
        var sessionEnumerator = sessionManager.GetSessionEnumerator();
        foreach (var session in sessionEnumerator)
        {
            // looks through all the active sessions and looks for "spotify" in the `Process` toString

            var sesInf = session.QueryInterface<AudioSessionControl2>();
            if (sesInf.Process.ToString().ToLower().Contains("spotify"))
            {
                Console.WriteLine("Spotify session found");
                Console.WriteLine("Process ID: " + sesInf.ProcessID);
                Spotifysession = session;
                break;
            }
        }
        if (Spotifysession == null)
        {
            Console.WriteLine("Spotify not found");
            Console.WriteLine("either it's not running or it doesn't have an active audio session, make sure it's showing in the volume mixer");
            Console.WriteLine("try playing something and try again");
            Console.WriteLine("Shutting down...");
            Environment.Exit(1);
        }
    }

    static void CreateSpotifyAPI()
    {
        // getting access token
        auth = new AuthorizationCodeAuth(
            clientID,
            clientSecret,
            redirectURL,
            redirectURL,
            Scope.UserReadCurrentlyPlaying
        );
        auth.AuthReceived += async (sender, payload) =>
        {
            auth.Stop();
            Console.WriteLine("Setting access token...");
            token = await auth.ExchangeCode(payload.Code);
            // creating spotifyAPI
            spotifyAPI = new SpotifyWebAPI()
            {
                TokenType = token.TokenType,
                AccessToken = token.AccessToken
            };
            Console.WriteLine("Token expires at: " + DateTime.Now.AddSeconds(token.ExpiresIn));
            Console.WriteLine("don't worry we'll refresh it automatically");
        };
        // prompt the user to authorize
        Console.WriteLine("Connecting to Spotify API...");
        Console.WriteLine("Please authorize the app in the borwser window that just opened");
        auth.Start();
        auth.OpenBrowser();
    }

    static async Task RefreshToken()
    {
        Console.WriteLine(String.Format("\r{0,-50}","Refreshing access token..."));
        Token newToken = await auth.RefreshToken(token.RefreshToken);
        spotifyAPI.AccessToken = newToken.AccessToken;
        spotifyAPI.TokenType = newToken.TokenType;
        token = newToken;
        Console.WriteLine("Access token refreshed at "+ DateTime.Now);
        Console.WriteLine("Token expires at " + DateTime.Now.AddSeconds(token.ExpiresIn));
    }

    static void SetMute(bool mute)
    {
        var spotifyVolume = Spotifysession.QueryInterface<SimpleAudioVolume>();
        spotifyVolume.IsMuted = mute;
    }
    
    static void SetVolume(float vol)
    {
        var spotifyVolume = Spotifysession.QueryInterface<SimpleAudioVolume>();
        spotifyVolume.MasterVolume = vol;
    }

    private static AudioSessionManager2 GetDefaultAudioSessionManager2(DataFlow dataFlow)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
        var sessionManager = AudioSessionManager2.FromMMDevice(device);
        return sessionManager;
    }
}