using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // The Luna Optimizer in a web browser while Unity is open. Unity serves one page (Editor/Web/LunaOptimizer.html)
    // and a JSON API (LunaOptimizerWebApi) on 127.0.0.1 only; the page is a remote control: every action runs here, in the editor,
    // through the same hub views as the window, so results and saves are identical.
    // Requests are received on a background thread and handled on Unity's main thread (EditorApplication.update).
    // Every API call must carry the key printed into the served page, so other websites cannot drive it.
    [InitializeOnLoad]
    public static class LunaOptimizerWebServer
    {
        const string MENU_OPEN = "Supercent/Luna Optimizer (Browser)";
        const string MENU_STOP = "Supercent/Stop Luna Optimizer Browser Server";
        const string RUNNING_PREF = "LunaOptimizer.WebServer.";   // + project path: EditorPrefs are shared by every project
        // Away from the 8xxx ports local preview servers (python -m http.server, Luna build previews) commonly use.
        const int FIRST_PORT = 47790;
        const int PORT_ATTEMPTS = 10;
        const string PAGE_FILE = "Editor/Web/LunaOptimizer.html";
        const string TOKEN_MARK = "__LO_TOKEN__";

        static HttpListener listener;
        static Thread acceptThread;
        static string token;
        static int port;
        static readonly ConcurrentQueue<HttpListenerContext> requests = new ConcurrentQueue<HttpListenerContext>();

        // 1×1 transparent PNG answered on /ping, so the launcher page (opened before Unity) can tell the server is up.
        static readonly byte[] PING_PNG = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

        static string RunningPref
        {
            get { return RUNNING_PREF + Path.GetDirectoryName(Application.dataPath); }
        }

        static LunaOptimizerWebServer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            // Keep serving across script reloads while the user has it on. Started right here: delayCall may not run
            // until Unity gets focus, which left the browser page without a server after a recompile.
            if (EditorPrefs.GetBool(RunningPref, false))
                Start();
        }

        public static bool IsRunning
        {
            get { return listener != null && listener.IsListening; }
        }

        public static string Url
        {
            get { return "http://127.0.0.1:" + port + "/"; }
        }

        [MenuItem(MENU_OPEN, false, 1)]
        public static void OpenInBrowser()
        {
            if (!IsRunning && !Start())
            {
                EditorUtility.DisplayDialog("Luna Optimizer (Browser)", "Could not start the local server on ports " + FIRST_PORT + "-" + (FIRST_PORT + PORT_ATTEMPTS - 1) + ". See the Console.", "OK");
                return;
            }
            EditorPrefs.SetBool(RunningPref, true);
            Application.OpenURL(Url);
        }

        [MenuItem(MENU_STOP, false, 2)]
        static void StopFromMenu()
        {
            EditorPrefs.SetBool(RunningPref, false);
            Stop();
        }

        [MenuItem(MENU_STOP, true)]
        static bool CanStop()
        {
            return IsRunning;
        }

        static bool Start()
        {
            if (IsRunning)
                return true;
            token = Guid.NewGuid().ToString("N");
            for (int i = 0; i < PORT_ATTEMPTS; i++)
            {
                var candidate = FIRST_PORT + i;
                // HttpListener can bind 127.0.0.1 while another server holds the same port on IPv6 (::); browsers then
                // split one page's requests between the two. Skip any port where something already answers.
                if (InUse(candidate))
                    continue;
                var attempt = new HttpListener();
                attempt.Prefixes.Add("http://127.0.0.1:" + candidate + "/");
                try
                {
                    attempt.Start();
                }
                catch (Exception)
                {
                    attempt.Close();
                    continue;
                }
                listener = attempt;
                port = candidate;
                acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Luna Optimizer web server" };
                acceptThread.Start();
                EditorApplication.update += HandleRequests;
                Debug.Log("Luna Optimizer is open in the browser at " + Url + " (this Mac only; stops with Unity).");
                return true;
            }
            Debug.LogWarning("Luna Optimizer (Browser): ports " + FIRST_PORT + "-" + (FIRST_PORT + PORT_ATTEMPTS - 1) + " are all in use.");
            return false;
        }

        static bool InUse(int candidate)
        {
            return Answers(IPAddress.Loopback, candidate) || Answers(IPAddress.IPv6Loopback, candidate);
        }

        static bool Answers(IPAddress address, int candidate)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient(address.AddressFamily))
                {
                    var connect = client.ConnectAsync(address, candidate);
                    return connect.Wait(200) && client.Connected;
                }
            }
            catch (Exception)
            {
                return false;   // Refused or unreachable: free.
            }
        }

        static void Stop()
        {
            EditorApplication.update -= HandleRequests;
            var current = listener;
            listener = null;
            if (current != null)
            {
                try
                {
                    current.Stop();
                    current.Close();
                }
                catch (Exception)
                {
                    // Already closed.
                }
            }
            HttpListenerContext pending;
            while (requests.TryDequeue(out pending))
            {
                try { pending.Response.Abort(); } catch (Exception) { }
            }
        }

        static void AcceptLoop()
        {
            while (true)
            {
                var current = listener;
                if (current == null || !current.IsListening)
                    return;
                try
                {
                    requests.Enqueue(current.GetContext());
                }
                catch (Exception)
                {
                    return;   // Stopped.
                }
            }
        }

        // Main thread. Actions may show Unity progress bars; the browser waits for the answer.
        static void HandleRequests()
        {
            HttpListenerContext context;
            for (int handled = 0; handled < 8 && requests.TryDequeue(out context); handled++)
            {
                try
                {
                    Handle(context);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    TryRespond(context, 500, "application/json", MiniJson.Serialize(new Dictionary<string, object> { { "ok", false }, { "message", exception.Message } }));
                }
            }
        }

        static void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var host = request.Url.Host;
            // Only this machine, and only pages served from here (blocks DNS rebinding and cross-site posts).
            if (!request.IsLocal || (host != "127.0.0.1" && host != "localhost"))
            {
                TryRespond(context, 403, "text/plain", "Forbidden");
                return;
            }
            var path = request.Url.AbsolutePath;
            if (path == "/" || path == "/index.html")
            {
                TryRespond(context, 200, "text/html; charset=utf-8", Page());
                return;
            }
            if (path == "/ping")
            {
                TryRespondBytes(context, 200, "image/png", PING_PNG);
                return;
            }
            if (path == "/favicon.ico")
            {
                TryRespond(context, 204, "text/plain", string.Empty);
                return;
            }
            var key = request.Headers["X-LO-Token"] ?? request.QueryString["key"];
            if (key != token)
            {
                TryRespond(context, 403, "application/json", "{\"ok\":false,\"message\":\"Session expired: reload the page.\"}");
                return;
            }
            if (path == "/api/action")
            {
                if (request.HttpMethod != "POST")
                {
                    TryRespond(context, 405, "text/plain", "POST only");
                    return;
                }
                string body;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();
                var action = JsonUtility.FromJson<LunaOptimizerWebApi.ActionRequest>(body);
                TryRespond(context, 200, "application/json", MiniJson.Serialize(LunaOptimizerWebApi.Run(action)));
                return;
            }
            var response = path.StartsWith("/api/", StringComparison.Ordinal)
                ? LunaOptimizerWebApi.Get(path, request.QueryString)
                : LunaOptimizerWebApi.Text(404, "Not found");
            var bytes = response.FilePath != null ? File.ReadAllBytes(response.FilePath) : response.Body ?? new byte[0];
            TryRespondBytes(context, response.Status, response.ContentType, bytes);
        }

        static string Page()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LunaOptimizerWebServer).Assembly);
            var file = package != null ? Path.Combine(package.resolvedPath, PAGE_FILE) : null;
            if (file == null || !File.Exists(file))
                return "<h1>Luna Optimizer</h1><p>Page not found: " + PAGE_FILE + "</p>";
            return File.ReadAllText(file).Replace(TOKEN_MARK, token);
        }

        // ---------------------------------------------------------------- responses

        static void TryRespond(HttpListenerContext context, int status, string contentType, string text)
        {
            TryRespondBytes(context, status, contentType, Encoding.UTF8.GetBytes(text));
        }

        static void TryRespondBytes(HttpListenerContext context, int status, string contentType, byte[] bytes)
        {
            try
            {
                var response = context.Response;
                response.StatusCode = status;
                response.ContentType = contentType;
                response.Headers["Cache-Control"] = "no-store";
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
                response.OutputStream.Close();
            }
            catch (Exception)
            {
                // The browser went away.
            }
        }
    }
}
