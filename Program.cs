using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using SpotifyAPI.Web.Models;

using CSCore.CoreAudioAPI;
using MMDeviceEnumerator = CSCore.CoreAudioAPI.MMDeviceEnumerator;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using System.Diagnostics;

class Program
{

    static AudioSessionControl spotifyAudioSession = null;
    static Process spotifyProcess = null;

    const int TIMEOUT_SECONDS = 60;
    static String clientID;
    static String clientSecret;
    const String redirectURL = "http://localhost:4002";
    static SpotifyWebAPI spotifyAPI;
    static AuthorizationCodeAuth auth;
    static Token token;
    static bool isAuthServerOn = false;

    public static void Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No/invalid paramaters detected");
            Console.WriteLine("When running, pass the clientID and the clientSecret as a paramaters to avoid entering them in the browser everytime");
            Console.WriteLine("Refer to the README file for instructions");
            Console.WriteLine("");
            Console.ForegroundColor = ConsoleColor.Gray;
        }
        else
        {
            clientID = args[0];
            clientSecret = args[1];
        }

        // async Main()
        MainAsync().GetAwaiter().GetResult();
    }

    static async Task MainAsync()
    {
        // Finding Spotify audio session
        FindAudioSession();

        //getting access token
        await CreateSpotifyAPI();

        await AppLoop();
    }

    static async Task AppLoop()
    {
        String status = "";
        int errorsCount = 0;
        bool pauseMode = false;
        DateTime startTime = DateTime.Now;
        int sleepTime = TIMEOUT_SECONDS * 1000; // default sleep duration = 5s

        PlaybackContext current;

        while (true)
        {

            if(spotifyAudioSession == null || spotifyProcess.HasExited)
            {
                Console.Write("\rSpotify isn't running. Looking for new process");
                FindAudioSession(isSilent: true);
                continue;
            }

            if (spotifyAPI != null)
            {

                if (!pauseMode)
                {
                    if (token.IsExpired() && !isAuthServerOn) await RefreshToken(isSilent: true);
                    current = spotifyAPI.GetPlayingTrack("US&additional_types=episode");

                    if (current.HasError())
                    {   // Error; didn't get the track
                        errorsCount++;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Error Status: " + current.Error.Status);
                        Console.WriteLine("Error Msg: " + current.Error.Message);
                        sleepTime = 10000;

                        if (errorsCount % 5 == 0 && errorsCount != 0)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkRed;
                            Console.WriteLine("--------------------------------------------");
                            Console.WriteLine("WARNING: " + errorsCount + " errors in a row");
                            Console.WriteLine("--------------------------------------------");
                        }
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }
                    else
                    {   // Got the track
                        errorsCount = 0;
                        if (current.IsPlaying)
                        {
                            if (current.CurrentlyPlayingType == TrackType.Ad)
                            {
                                SetMute(true);
                                status = "Muted";
                                sleepTime = 1500;
                            }
                            else
                            {
                                SetMute(false);
                                status = String.Format("Playing: {0}", current.Item.Name);

                                // set the sleep time to the duration divided by numOfChecks if it's less than the remiaing time
                                // so basically if it's an episode check every tenth of the episode's duration (every third for track) to see if it's still running
                                // example: an episode that's 50 mins long. will check every 5 mins
                                int numOfChecks = current.CurrentlyPlayingType == TrackType.Track ? 3 : 10;
                                if (current.Item.DurationMs - current.ProgressMs < current.Item.DurationMs / numOfChecks)
                                    sleepTime = current.Item.DurationMs - current.ProgressMs;
                                else
                                    sleepTime = current.Item.DurationMs / numOfChecks;
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

                    // listen to spotify and if the volume is more than 0 (playing someting) then set pauseMode to false
                    // using 0.001f because sometimes it reads 6e-10 even when there's nothing playing ¯\_(ツ)_/¯
                    if (spotifyAudioSession.QueryInterface<AudioMeterInformation>().PeakValue > 0.0001f)
                    {
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
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(String.Format("Timeout: connecting took too long: {0:0.00} seconds", (DateTime.Now - startTime).TotalSeconds));
                    Console.WriteLine("Shutting down...");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Environment.Exit(1);
                }

            }

            if (!status.Equals("")) Console.Write(String.Format("\r{0,-80}", status));
            Thread.Sleep(sleepTime);
        }
    }

    static void FindAudioSession(bool isSilent = false)
    {
        if(!isSilent) Console.WriteLine("Finding Spotify audio session...");
        var sessionManager = GetDefaultAudioSessionManager2(DataFlow.Render);
        var sessionEnumerator = sessionManager.GetSessionEnumerator();
        foreach (var session in sessionEnumerator)
        {
            // looks through all the active sessions and looks for "spotify" in the `Process` toString
            try
            {
                var sesInf = session.QueryInterface<AudioSessionControl2>();
                if (sesInf.Process.ToString().ToLower().Contains("spotify"))
                {
                    Console.Write("\r");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(String.Format("{0,-80}", "Spotify session found"));
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("Process ID: " + sesInf.ProcessID);
                    spotifyAudioSession = session;
                    spotifyProcess = spotifyAudioSession.QueryInterface<AudioSessionControl2>().Process;
                    break;
                }
            }
            catch (InvalidOperationException)
            {
                // this can happen if the current session (in the loop, not Spotify) exited before we check its name
                // this will resault in the following "System.InvalidOperationException: Process has exited, so the requested information is not available."
                continue;
            }
            
        }
        if (spotifyAudioSession == null)
        {
            if (!isSilent) Console.ForegroundColor = ConsoleColor.Red;
            if (!isSilent) Console.WriteLine("Spotify not found.");
            if (!isSilent) Console.WriteLine("either it's not running or it doesn't have an active audio session, try playing something");
            Console.ForegroundColor = ConsoleColor.Gray;
        }
    }

    static async Task CreateSpotifyAPI()
    {

        auth = new AuthorizationCodeAuth(
            clientID,
            clientSecret,
            redirectURL,
            redirectURL,
            Scope.UserReadCurrentlyPlaying
        );

        if (File.Exists("AccessToken.json"))
        {
            Console.WriteLine("Loading files...");
            String fileContent = File.ReadAllText("AccessToken.json");
            try
            {
                if (fileContent != "")
                {
                    token = JsonConvert.DeserializeObject<Token>(fileContent);
                    Console.WriteLine("Token acquired");
                    Console.WriteLine("Connecting to Spotify API...");
                    spotifyAPI = new SpotifyWebAPI()
                    {
                        TokenType = token.TokenType,
                        AccessToken = token.AccessToken
                    };
                    Console.WriteLine("Connected");
                    if (token.IsExpired()) await RefreshToken();
                    return;
                }
                else
                {
                    Console.WriteLine("File is empty");
                }
            }
            catch (Exception) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("An error occurred in loading files");
                Console.ForegroundColor = ConsoleColor.Gray;
            }
        }

        // if access token isn't saved or couldn't be loaded then go therough the authorization again
        // getting access token
        Console.WriteLine("Attempting authorization through browser...");
        
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
            Console.WriteLine("Connected");
            Console.WriteLine("New '" + token.TokenType + "' access token acquired at " + DateTime.Now);
            Console.WriteLine("Token expires at: " + DateTime.Now.AddSeconds(token.ExpiresIn));
            Console.WriteLine("don't worry it'll refresh automatically");

            SaveTokenToFile();
            isAuthServerOn = false;
        };
        Console.WriteLine("Connecting to Spotify API...");
        auth.Start();
        isAuthServerOn = true;
        auth.OpenBrowser();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Please authorize the app in the borwser window that just opened");
        Console.ForegroundColor = ConsoleColor.Gray;

    }

    static async Task RefreshToken(bool isSilent = false)
    {
        if(!isSilent) Console.WriteLine(String.Format("\r{0,-80}", "Refreshing access token..."));
        Token newToken = await auth.RefreshToken(token.RefreshToken);
        if (!newToken.HasError())
        {
            spotifyAPI.AccessToken = newToken.AccessToken;
            spotifyAPI.TokenType = newToken.TokenType;
            
            token.AccessToken = newToken.AccessToken;
            token.ExpiresIn = newToken.ExpiresIn;
            token.CreateDate = newToken.CreateDate;

            if (!isSilent) Console.WriteLine("New '" + token.TokenType + "' access token acquired at " + DateTime.Now);

            SaveTokenToFile(isSilent);
            if (!isSilent) Console.WriteLine("");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("TOKEN ERROR");
            Console.WriteLine("Error:\t\t" + newToken.Error);
            Console.WriteLine("Description:\t" + newToken.ErrorDescription);
            Console.ForegroundColor = ConsoleColor.Gray;
            if (newToken.ErrorDescription == "Refresh token revoked") {
                File.WriteAllText("AccessToken.json", "");
                if (!isAuthServerOn)
                    await CreateSpotifyAPI();
            }
            else 
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Shutting down...");
                Console.ForegroundColor = ConsoleColor.Gray;
                Environment.Exit(0);
            }
        }
    }

    private static void SaveTokenToFile(bool isSilent = false)
    {
        if (!isSilent) Console.WriteLine("Saving token to file...");
        File.WriteAllText("AccessToken.json", JsonConvert.SerializeObject(token));
    }

    private static void SetMute(bool mute)
    {
        var spotifyVolume = spotifyAudioSession.QueryInterface<SimpleAudioVolume>();
        spotifyVolume.IsMuted = mute;
    }

    static void SetVolume(float vol)
    {
        var spotifyVolume = spotifyAudioSession.QueryInterface<SimpleAudioVolume>();
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