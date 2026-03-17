Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.Hosting
Imports Npgsql
Imports System.IO
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Text.Json
Imports System.Text

Module Program
    Private ConnectionString As String = String.Empty
    Private SmsConfig As SmsSettings = New SmsSettings()
    Private ReadOnly JsonOptions As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True
    }
    Private ReadOnly PageRouteMap As New Dictionary(Of String, String) From {
        {"/index", "index.html"},
        {"/products", "products.html"},
        {"/learn-more", "learn-more.html"},
        {"/search", "search.html"},
        {"/cart", "cart.html"},
        {"/login", "login.html"},
        {"/create-account", "create-account.html"},
        {"/logged-in", "logged-in.html"},
        {"/create-memorial", "create-memorial.html"},
        {"/matt-fraser", "matt-fraser.html"}
    }

    Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)
        ConnectionString = builder.Configuration("ConnectionStrings:DefaultConnection")
        If String.IsNullOrWhiteSpace(ConnectionString) Then
            ConnectionString = builder.Configuration("ConnectionStrings__DefaultConnection")
        End If
        If String.IsNullOrWhiteSpace(ConnectionString) Then
            ConnectionString = "Host=postgres;Port=5432;Database=AppDb;Username=postgres;Password=YourStrong@Passw0rd"
        End If

        Dim app = builder.Build()

        If app.Environment.IsDevelopment() Then
            app.UseDeveloperExceptionPage()
        End If

        app.UseStaticFiles()
        app.UseRouting()

        Dim credentialsPath = Path.Combine(app.Environment.ContentRootPath, "admin-credentials.json")
        Dim smsSettingsPath = Path.Combine(app.Environment.ContentRootPath, "sms-settings.json")
        Dim adminCredentials = LoadOrCreateAdminCredentials(credentialsPath)
        SmsConfig = LoadSmsSettings(smsSettingsPath)
        EnsureDatabaseReady(ConnectionString, adminCredentials)

        app.MapGet("/", Function() Results.Redirect("/index.html"))
        For Each routeEntry In PageRouteMap
            Dim route = routeEntry.Key
            Dim page = routeEntry.Value
            app.MapGet(route, Function() Results.File(Path.Combine(app.Environment.ContentRootPath, "wwwroot", page), "text/html"))
        Next

        app.MapGet("/api/health", Function() Results.Ok(New ApiResponse With {.Success = True, .Message = "API is running."}))

        app.MapPost("/api/auth/login", AddressOf HandleLoginRequest)
        app.MapPost("/api/auth/register", AddressOf HandleRegisterRequest)
        app.MapPost("/api/ai/biography-assist", AddressOf HandleBiographyAssistRequest)

        app.Run()
    End Sub

    Private Async Function HandleBiographyAssistRequest(context As HttpContext) As Task
        Dim request As BiographyAssistRequest = Nothing
        Try
            request = Await JsonSerializer.DeserializeAsync(Of BiographyAssistRequest)(context.Request.Body, JsonOptions)
        Catch
            request = Nothing
        End Try

        If request Is Nothing OrElse String.IsNullOrWhiteSpace(request.BiographyText) Then
            context.Response.StatusCode = StatusCodes.Status400BadRequest
            Await context.Response.WriteAsJsonAsync(New BiographyAssistResponse With {
                .Success = False,
                .Message = "Biography text is required."
            })
            Return
        End If

        Dim apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
        If String.IsNullOrWhiteSpace(apiKey) Then
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable
            Await context.Response.WriteAsJsonAsync(New BiographyAssistResponse With {
                .Success = False,
                .Message = "AI assist is not configured. Set OPENROUTER_API_KEY in the webapp environment."
            })
            Return
        End If

        Dim modelName = "meta-llama/llama-3.1-8b-instruct"
        Dim prompt =
            "You are an expert and compassionate biographer and memorial writer for a platform called LifeQR. Your task is to take raw notes, memories, and dates provided by a user and weave them into a beautiful, respectful, and touching biography that honors a person's life." & vbLf &
            "Guidelines for your writing:" & vbLf &
            "Match the Perspective: Read the user's input carefully. If they write directly to the person (for example, ""You were the best brother...""), write the biography in that same intimate, letters-to-heaven style. If they provide traditional facts, write a third-person narrative (he/she/they)." & vbLf &
            "Match the Language: Always write the biography in the exact same language the user used in their input details." & vbLf &
            "Write as a Flowing Narrative: Create a continuous, flowing biography without any section headers, subtitles, or bold formatting. Write it as one cohesive story that naturally moves from their early life through their journey to their legacy." & vbLf &
            "Keep it Concise: Write a biography that is 5 paragraphs long (approximately 400-500 words total). Be meaningful and touching, covering their life story in a well-structured narrative." & vbLf &
            "Adapt to the Selected Tone: The user will select one of three specific tones for the biography. You must strictly adjust your vocabulary, pacing, and emotional resonance to match their choice:" & vbLf &
            "Warm and gentle: Focus on love, comfort, quiet moments, and deep emotional connections. Use soft, soothing, and intimate language." & vbLf &
            "Formal and respectful: Focus on dignity, legacy, achievements, and honor. Use elegant, traditional, and highly structured language (similar to a classic, prestigious obituary)." & vbLf &
            "Celebratory and uplifting: Focus on joy, laughter, vibrant memories, and the positive light the person brought into the world. Use bright, energetic, and gratitude-filled language." & vbLf &
            "Do Not Invent Information: Do not make up facts, relatives, or hobbies. Only beautifully expand upon and connect the information the user actually provides." & vbLf &
            "Do Not Use Markdown: Do not use any markdown formatting like asterisks, underscores, hashtags, or bold text. Write in plain, elegant prose only." & vbLf &
            "Biography text:" & vbLf &
            request.BiographyText.Trim()

        Dim payload As New Dictionary(Of String, Object) From {
            {"model", modelName},
            {"messages", New Object() {
                New Dictionary(Of String, String) From {
                    {"role", "system"},
                    {"content", "You are a compassionate memorial writing assistant."}
                },
                New Dictionary(Of String, String) From {
                    {"role", "user"},
                    {"content", prompt}
                }
            }}
        }

        Using client As New HttpClient()
            client.DefaultRequestHeaders.Authorization = New AuthenticationHeaderValue("Bearer", apiKey)
            client.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:8080")
            client.DefaultRequestHeaders.Add("X-Title", "LifeQR")

            Dim payloadJson = JsonSerializer.Serialize(payload)
            Using content As New StringContent(payloadJson, Encoding.UTF8, "application/json")
                Dim response As HttpResponseMessage = Nothing
                Dim providerCallError As Exception = Nothing
                Try
                    response = Await client.PostAsync("https://openrouter.ai/api/v1/chat/completions", content)
                Catch ex As Exception
                    providerCallError = ex
                End Try

                If providerCallError IsNot Nothing Then
                    context.Response.StatusCode = StatusCodes.Status502BadGateway
                    Await context.Response.WriteAsJsonAsync(New BiographyAssistResponse With {
                        .Success = False,
                        .Message = "Failed to reach AI provider: " & providerCallError.Message
                    })
                    Return
                End If

                Dim responseText = Await response.Content.ReadAsStringAsync()
                If Not response.IsSuccessStatusCode Then
                    context.Response.StatusCode = StatusCodes.Status502BadGateway
                    Await context.Response.WriteAsJsonAsync(New BiographyAssistResponse With {
                        .Success = False,
                        .Message = "AI provider error: " & responseText
                    })
                    Return
                End If

                Dim draftText = String.Empty
                Try
                    Using doc = JsonDocument.Parse(responseText)
                        draftText = doc.RootElement.GetProperty("choices")(0).GetProperty("message").GetProperty("content").GetString()
                    End Using
                Catch
                    draftText = String.Empty
                End Try

                If Not String.IsNullOrWhiteSpace(draftText) Then
                    draftText = draftText.Replace("**", String.Empty).Replace("__", String.Empty)
                End If

                If String.IsNullOrWhiteSpace(draftText) Then
                    context.Response.StatusCode = StatusCodes.Status502BadGateway
                    Await context.Response.WriteAsJsonAsync(New BiographyAssistResponse With {
                        .Success = False,
                        .Message = "AI response was empty."
                    })
                    Return
                End If

                context.Response.StatusCode = StatusCodes.Status200OK
                Await context.Response.WriteAsJsonAsync(New BiographyAssistResponse With {
                    .Success = True,
                    .Message = "Biography draft generated.",
                    .DraftText = draftText
                })
            End Using
        End Using
    End Function

    Private Async Function HandleLoginRequest(context As HttpContext) As Task
        Dim request As LoginRequest = Nothing
        Try
            request = Await JsonSerializer.DeserializeAsync(Of LoginRequest)(context.Request.Body, JsonOptions)
        Catch
            request = Nothing
        End Try

        If request Is Nothing OrElse String.IsNullOrWhiteSpace(request.Email) OrElse String.IsNullOrWhiteSpace(request.Password) Then
            context.Response.StatusCode = StatusCodes.Status400BadRequest
            Await context.Response.WriteAsJsonAsync(New ApiResponse With {.Success = False, .Message = "Email and password are required."})
            Return
        End If

        Using db As New NpgsqlConnection(ConnectionString)
            db.Open()
            Const sql As String = "SELECT full_name, password, role FROM users WHERE email = @email LIMIT 1;"
            Using cmd As New NpgsqlCommand(sql, db)
                cmd.Parameters.AddWithValue("email", request.Email.Trim().ToLowerInvariant())
                Using reader = cmd.ExecuteReader()
                    If Not reader.Read() Then
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized
                        Await context.Response.WriteAsJsonAsync(New ApiResponse With {.Success = False, .Message = "Email is incorrect."})
                        Return
                    End If

                    Dim fullName = If(reader.IsDBNull(0), request.Email.Trim(), reader.GetString(0))
                    Dim storedPassword = reader.GetString(1)
                    Dim role = reader.GetString(2)

                    If Not String.Equals(storedPassword, request.Password, StringComparison.Ordinal) Then
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized
                        Await context.Response.WriteAsJsonAsync(New ApiResponse With {.Success = False, .Message = "Password is incorrect."})
                        Return
                    End If

                    context.Response.StatusCode = StatusCodes.Status200OK
                    Await context.Response.WriteAsJsonAsync(New ApiResponse With {
                        .Success = True,
                        .Message = "Login successful.",
                        .Username = fullName,
                        .Role = role
                    })
                    Return
                End Using
            End Using
        End Using

        context.Response.StatusCode = StatusCodes.Status500InternalServerError
        Await context.Response.WriteAsJsonAsync(New ApiResponse With {.Success = False, .Message = "Unexpected login error."})
    End Function

    Private Async Function HandleRegisterRequest(context As HttpContext) As Task
        Dim request As RegisterRequest = Nothing
        Try
            request = Await JsonSerializer.DeserializeAsync(Of RegisterRequest)(context.Request.Body, JsonOptions)
        Catch
            request = Nothing
        End Try

        If request Is Nothing Then
            context.Response.StatusCode = StatusCodes.Status400BadRequest
            Await context.Response.WriteAsJsonAsync(New ApiResponse With {.Success = False, .Message = "Invalid request payload."})
            Return
        End If

        Dim fullName = If(request.Name, String.Empty).Trim()
        Dim dob = If(request.Dob, String.Empty).Trim()
        Dim email = If(request.Email, String.Empty).Trim().ToLowerInvariant()
        Dim password = If(request.Password, String.Empty)

        If fullName = String.Empty OrElse dob = String.Empty OrElse email = String.Empty OrElse password = String.Empty Then
            context.Response.StatusCode = StatusCodes.Status400BadRequest
            Await context.Response.WriteAsJsonAsync(New ApiResponse With {.Success = False, .Message = "Name, DOB, email, and password are required."})
            Return
        End If

        Dim parsedDob As DateTime
        If Not DateTime.TryParse(dob, parsedDob) Then
            context.Response.StatusCode = StatusCodes.Status400BadRequest
            Await context.Response.WriteAsJsonAsync(New ApiResponse With {.Success = False, .Message = "DOB is invalid."})
            Return
        End If

        Using db As New NpgsqlConnection(ConnectionString)
            db.Open()

            Const existingUserSql As String = "SELECT 1 FROM users WHERE email = @email LIMIT 1;"
            Using existingUserCmd As New NpgsqlCommand(existingUserSql, db)
                existingUserCmd.Parameters.AddWithValue("email", email)
                Dim existing = existingUserCmd.ExecuteScalar()
                If existing IsNot Nothing Then
                    context.Response.StatusCode = StatusCodes.Status409Conflict
                    Await context.Response.WriteAsJsonAsync(New ApiResponse With {
                        .Success = False,
                        .Message = "An account with this email already exists."
                    })
                    Return
                End If
            End Using

            Const insertSql As String =
                "INSERT INTO users (username, password, role, full_name, dob, email) " &
                "VALUES (@username, @password, 'user', @full_name, @dob, @email);"

            Using insertCmd As New NpgsqlCommand(insertSql, db)
                insertCmd.Parameters.AddWithValue("username", email)
                insertCmd.Parameters.AddWithValue("password", password)
                insertCmd.Parameters.AddWithValue("full_name", fullName)
                insertCmd.Parameters.AddWithValue("dob", parsedDob.Date)
                insertCmd.Parameters.AddWithValue("email", email)
                insertCmd.ExecuteNonQuery()
            End Using

            context.Response.StatusCode = StatusCodes.Status200OK
            Await context.Response.WriteAsJsonAsync(New ApiResponse With {
                .Success = True,
                .Message = "Registration successful."
            })
        End Using
    End Function

    Private Function LoadOrCreateAdminCredentials(filePath As String) As AdminCredentials
        If File.Exists(filePath) Then
            Dim json = File.ReadAllText(filePath)
            Dim existing = JsonSerializer.Deserialize(Of AdminCredentials)(json)
            If existing IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(existing.Username) AndAlso Not String.IsNullOrWhiteSpace(existing.Password) Then
                Return existing
            End If
        End If

        Dim defaults As New AdminCredentials With {
            .Username = "Mario Salinas",
            .Password = "Mario Salinas"
        }

        Dim jsonOptions As New JsonSerializerOptions With {.WriteIndented = True}
        File.WriteAllText(filePath, JsonSerializer.Serialize(defaults, jsonOptions))
        Return defaults
    End Function

    Private Function LoadSmsSettings(filePath As String) As SmsSettings
        If File.Exists(filePath) Then
            Dim json = File.ReadAllText(filePath)
            Dim existing = JsonSerializer.Deserialize(Of SmsSettings)(json)
            If existing IsNot Nothing Then
                Return existing
            End If
        End If

        Dim defaults As New SmsSettings()
        Dim jsonOptions As New JsonSerializerOptions With {.WriteIndented = True}
        File.WriteAllText(filePath, JsonSerializer.Serialize(defaults, jsonOptions))
        Return defaults
    End Function

    Private Sub EnsureDatabaseReady(connectionString As String, credentials As AdminCredentials)
        Using db As New NpgsqlConnection(connectionString)
            db.Open()

            Const schemaSql As String =
                "CREATE TABLE IF NOT EXISTS users (" &
                "id SERIAL PRIMARY KEY," &
                "username TEXT NOT NULL UNIQUE," &
                "password TEXT NOT NULL," &
                "role TEXT NOT NULL DEFAULT 'user'," &
                "first_name TEXT," &
                "last_name TEXT," &
                "email TEXT UNIQUE," &
                "phone_number TEXT UNIQUE," &
                "phone_verified BOOLEAN NOT NULL DEFAULT FALSE," &
                "email_verified BOOLEAN NOT NULL DEFAULT FALSE," &
                "created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()" &
                ");" &
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS first_name TEXT;" &
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS last_name TEXT;" &
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS email TEXT UNIQUE;" &
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS phone_number TEXT UNIQUE;" &
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS phone_verified BOOLEAN NOT NULL DEFAULT FALSE;" &
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS email_verified BOOLEAN NOT NULL DEFAULT FALSE;" &
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS full_name TEXT;" &
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS dob DATE;" &
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();" &
                "CREATE TABLE IF NOT EXISTS phone_verifications (" &
                "phone_number TEXT PRIMARY KEY," &
                "email TEXT NOT NULL," &
                "first_name TEXT NOT NULL," &
                "last_name TEXT NOT NULL," &
                "password TEXT NOT NULL," &
                "code TEXT NOT NULL," &
                "expires_at TIMESTAMPTZ NOT NULL," &
                "created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()" &
                ");"

            Using schemaCmd As New NpgsqlCommand(schemaSql, db)
                schemaCmd.ExecuteNonQuery()
            End Using

            Dim firstName = credentials.Username
            Dim lastName = String.Empty
            Dim nameParts = credentials.Username.Split(" ", 2, StringSplitOptions.RemoveEmptyEntries)
            If nameParts.Length > 0 Then
                firstName = nameParts(0)
            End If
            If nameParts.Length > 1 Then
                lastName = nameParts(1)
            End If

            Const seedSql As String =
                "INSERT INTO users (username, password, role, first_name, last_name, phone_verified) " &
                "VALUES (@username, @password, 'admin', @first_name, @last_name, TRUE) " &
                "ON CONFLICT (username) DO UPDATE SET password = EXCLUDED.password, role = 'admin', first_name = EXCLUDED.first_name, last_name = EXCLUDED.last_name, phone_verified = TRUE;"

            Using seedCmd As New NpgsqlCommand(seedSql, db)
                seedCmd.Parameters.AddWithValue("username", credentials.Username)
                seedCmd.Parameters.AddWithValue("password", credentials.Password)
                seedCmd.Parameters.AddWithValue("first_name", firstName)
                seedCmd.Parameters.AddWithValue("last_name", lastName)
                seedCmd.ExecuteNonQuery()
            End Using

            Const adminProfileSql As String =
                "UPDATE users SET full_name = @full_name, dob = COALESCE(dob, DATE '1970-01-01'), email = COALESCE(email, @email) WHERE username = @username;"
            Using adminProfileCmd As New NpgsqlCommand(adminProfileSql, db)
                adminProfileCmd.Parameters.AddWithValue("full_name", credentials.Username)
                adminProfileCmd.Parameters.AddWithValue("email", "admin@local.lifeqr")
                adminProfileCmd.Parameters.AddWithValue("username", credentials.Username)
                adminProfileCmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub
End Module

Public Class LoginRequest
    Public Property Email As String = String.Empty
    Public Property Password As String = String.Empty
End Class

Public Class RegisterRequest
    Public Property Name As String = String.Empty
    Public Property Dob As String = String.Empty
    Public Property Email As String = String.Empty
    Public Property Password As String = String.Empty
End Class

Public Class BiographyAssistRequest
    Public Property BiographyText As String = String.Empty
End Class

Public Class AdminCredentials
    Public Property Username As String = String.Empty
    Public Property Password As String = String.Empty
End Class

Public Class SmsSettings
    Public Property Provider As String = "Twilio"
    Public Property AccountSid As String = String.Empty
    Public Property AuthToken As String = String.Empty
    Public Property FromNumber As String = String.Empty
End Class

Public Class ApiResponse
    Public Property Success As Boolean
    Public Property Message As String = String.Empty
    Public Property Username As String = String.Empty
    Public Property Role As String = String.Empty
    Public Property VerificationCode As String = String.Empty
End Class

Public Class BiographyAssistResponse
    Public Property Success As Boolean
    Public Property Message As String = String.Empty
    Public Property DraftText As String = String.Empty
End Class
