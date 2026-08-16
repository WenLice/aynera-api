using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Elaris.Application.Features.Auth.Repositories;
using Elaris.Application.Features.Auth.Services.Interfaces;
using Elaris.Domain.Auth.Requests;
using Elaris.Domain.Auth.Responses;
using Elaris.Domain.Auth.Enums;
using Elaris.Domain.Common;
using Elaris.Domain.Photos.Responses;
using Elaris.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SixLabors.ImageSharp;

namespace Elaris.Api.IntegrationTests;

[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<AuthApiFactory>;

/// <summary>
/// Shared API factory for integration tests.
/// Uses Postgres database <c>elaris_test</c> (created by docker init or on first run when permitted).
/// </summary>
public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public CapturingSmsService Sms { get; } = new();
    public CapturingEmailService Email { get; } = new();

    public AuthApiFactory()
    {
        var baseConnection =
            Environment.GetEnvironmentVariable("ELARIS_TEST_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ELARIS_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Username=elaris;Password=elaris";

        const string databaseName = "elaris_test";
        EnsureDatabaseExists(baseConnection, databaseName);
        _connectionString = BuildDatabaseConnectionString(baseConnection, databaseName);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Elaris:Jwt:SigningKey", "test-signing-key-that-is-long-enough-32");
        builder.UseSetting("Elaris:Jwt:Issuer", "elaris-api");
        builder.UseSetting("ELARIS_JWT_SIGNING_KEY", "test-signing-key-that-is-long-enough-32");
        builder.UseSetting("ConnectionStrings:Elaris", _connectionString);
        builder.UseSetting("ELARIS_DB_CONNECTION", _connectionString);
        builder.UseSetting("Elaris:Email:VerifyLinkBaseUrl", "http://localhost:5173/verify-email");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISmsService>();
            services.AddSingleton<ISmsService>(Sms);
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Email);
        });
    }

    private static void EnsureDatabaseExists(string baseConnection, string databaseName)
    {
        var adminConnection = BuildDatabaseConnectionString(baseConnection, "elaris");
        try
        {
            using var connection = new NpgsqlConnection(adminConnection);
            connection.Open();

            using (var exists = new NpgsqlCommand(
                       "SELECT 1 FROM pg_database WHERE datname = @name",
                       connection))
            {
                exists.Parameters.AddWithValue("name", databaseName);
                if (exists.ExecuteScalar() is not null)
                {
                    return;
                }
            }

            using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            create.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw new InvalidOperationException(
                $"Postgres user cannot create database '{databaseName}'. " +
                $"Create it once (e.g. CREATE DATABASE {databaseName};) or use docker-compose init, " +
                "then re-run tests.",
                ex);
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                "Could not reach Postgres for integration tests. " +
                "Start it with: docker compose -f src/docker-compose.yml up -d postgres",
                ex);
        }
    }

    private static string BuildDatabaseConnectionString(string baseConnection, string databaseName)
    {
        var parts = baseConnection
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        parts.Add($"Database={databaseName}");
        return string.Join(';', parts);
    }
}

[Collection("Integration")]
public class AuthEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthApiFactory _factory;

    public AuthEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    private static CreateMemberRequest RegisterBody(
        string phone,
        string email = "member@example.com",
        string? password = null) =>
        new(
            phone,
            "Ada",
            "Lovelace",
            Gender.Female,
            new DateOnly(1990, 5, 15),
            "Mumbai",
            email,
            "Hindu",
            password);

    [Fact]
    public async Task RegisterLoginVerifySmsMe_RoundTrip()
    {
        var client = _factory.CreateClient();
        var phone = "9988776655";

        var registerResponse = await client.PostAsJsonAsync(
            "/users/register",
            RegisterBody(phone, "roundtrip@example.com"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/auth/login",
            new RequestMemberOtpRequest(phone));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var code = _factory.Sms.GetCode("+919988776655");
        Assert.False(string.IsNullOrWhiteSpace(code));

        var verifyResponse = await client.PostAsJsonAsync(
            "/auth/verifysms",
            new VerifyMemberOtpRequest(phone, code!));
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var tokensEnvelope = await verifyResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        Assert.NotNull(tokensEnvelope);
        Assert.True(tokensEnvelope!.Success);
        Assert.NotNull(tokensEnvelope.Data);
        Assert.False(string.IsNullOrWhiteSpace(tokensEnvelope.Data!.AccessToken));
        Assert.NotNull(tokensEnvelope.Data.Account.Profile);
        Assert.Equal("Ada", tokensEnvelope.Data.Account.Profile!.FirstName);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokensEnvelope.Data.AccessToken);

        var meResponse = await client.GetAsync("/users/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var meEnvelope = await meResponse.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.NotNull(meEnvelope);
        Assert.True(meEnvelope!.Success);
        Assert.NotNull(meEnvelope.Data);
        Assert.Equal("+919988776655", meEnvelope.Data!.Phone);
        Assert.True(meEnvelope.Data.PhoneConfirmed);
        Assert.Contains("member", meEnvelope.Data.Roles);
        Assert.NotNull(meEnvelope.Data.Profile);
    }

    [Fact]
    public async Task Login_WithoutRegister_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/auth/login",
            new RequestMemberOtpRequest("9111222333"));
        Assert.Equal(HttpStatusCode.NotFound, loginResponse.StatusCode);

        var fail = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<object?>>(JsonOptions);
        Assert.NotNull(fail);
        Assert.False(fail!.Success);
        Assert.Equal("user_not_found", fail.ErrorCode);
    }

    [Fact]
    public async Task Register_ThenDuplicate_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var phone = "8877665544";

        var createResponse = await client.PostAsJsonAsync(
            "/users/register",
            RegisterBody(phone, "new.member@example.com"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var envelope = await createResponse.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.NotNull(envelope);
        Assert.True(envelope!.Success);
        Assert.Equal("+918877665544", envelope.Data!.Phone);
        Assert.Equal("new.member@example.com", envelope.Data.Email);
        Assert.False(envelope.Data.PhoneConfirmed);
        Assert.Contains("member", envelope.Data.Roles);
        Assert.NotNull(envelope.Data.Profile);
        Assert.Equal("Ada", envelope.Data.Profile!.FirstName);
        Assert.Equal("Female", envelope.Data.Profile.Gender);
        Assert.Equal("Mumbai", envelope.Data.Profile.City);
        Assert.False(envelope.Data.EmailConfirmed);
        Assert.Equal("new.member@example.com", _factory.Email.LastEmail);
        Assert.False(string.IsNullOrWhiteSpace(_factory.Email.LastVerifyUrl));

        var duplicate = await client.PostAsJsonAsync(
            "/users/register",
            RegisterBody(phone, "dup@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var fail = await duplicate.Content.ReadFromJsonAsync<ApiResponse<object?>>(JsonOptions);
        Assert.NotNull(fail);
        Assert.False(fail!.Success);
        Assert.Equal("user_already_exists", fail.ErrorCode);
    }

    [Fact]
    public async Task Register_ThenVerifyEmail_ConfirmsEmail()
    {
        var client = _factory.CreateClient();
        var phone = "9444332211";

        var createResponse = await client.PostAsJsonAsync(
            "/users/register",
            RegisterBody(phone, "confirm.email@example.com"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.NotNull(created?.Data);
        Assert.False(created!.Data!.EmailConfirmed);

        var verifyUrl = _factory.Email.LastVerifyUrl;
        Assert.False(string.IsNullOrWhiteSpace(verifyUrl));

        var uri = new Uri(verifyUrl!);
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]), StringComparer.OrdinalIgnoreCase);

        var verifyResponse = await client.PostAsJsonAsync(
            "/auth/verifyemail",
            new ConfirmEmailRequest(Guid.Parse(query["userId"]), query["token"]));
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var confirmed = await verifyResponse.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.NotNull(confirmed?.Data);
        Assert.True(confirmed!.Data!.EmailConfirmed);
        Assert.Equal(created.Data.Id, confirmed.Data.Id);
    }

    [Fact]
    public async Task Register_Underage_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var body = new CreateMemberRequest(
            "7766554433",
            "Kid",
            "User",
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16)),
            "Delhi",
            "kid@example.com");

        var response = await client.PostAsJsonAsync("/users/register", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var fail = await response.Content.ReadFromJsonAsync<ApiResponse<object?>>(JsonOptions);
        Assert.NotNull(fail);
        Assert.False(fail!.Success);
        Assert.Equal("underage", fail.ErrorCode);
    }

    [Fact]
    public async Task DeleteAccount_ThenReregister_CreatesNewAccount()
    {
        var client = _factory.CreateClient();
        var phone = "6655443322";

        var createResponse = await client.PostAsJsonAsync(
            "/users/register",
            RegisterBody(phone));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.NotNull(created?.Data);

        var loginResponse = await client.PostAsJsonAsync(
            "/auth/login",
            new RequestMemberOtpRequest(phone));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var code = _factory.Sms.GetCode("+916655443322");
        var verifyResponse = await client.PostAsJsonAsync(
            "/auth/verifysms",
            new VerifyMemberOtpRequest(phone, code!));
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var tokens = await verifyResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.Data!.AccessToken);

        var deleteResponse = await client.DeleteAsync("/users/account");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;

        var reregister = await client.PostAsJsonAsync(
            "/users/register",
            RegisterBody(phone));
        Assert.Equal(HttpStatusCode.OK, reregister.StatusCode);

        var again = await reregister.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.NotNull(again?.Data);
        Assert.NotEqual(created.Data!.Id, again.Data!.Id);
        Assert.True(again.Data.IsActive);
        Assert.False(again.Data.IsDeleted);
        Assert.NotNull(again.Data.Profile);
    }

    [Fact]
    public async Task Login_EmptyPhone_ReturnsValidationEnvelope()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new { phone = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<object?>>(JsonOptions);
        Assert.NotNull(envelope);
        Assert.False(envelope!.Success);
        Assert.Equal("validation_failed", envelope.ErrorCode);
        Assert.NotNull(envelope.Errors);
    }

    [Fact]
    public async Task UploadListGetDeletePhotos_RoundTrip()
    {
        var client = _factory.CreateClient();
        var phone = "9333444555";

        await client.PostAsJsonAsync("/users/register", RegisterBody(phone, "photos@example.com"));

        var loginResponse = await client.PostAsJsonAsync("/auth/login", new RequestMemberOtpRequest(phone));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var code = _factory.Sms.GetCode("+919333444555");
        var verifyResponse = await client.PostAsJsonAsync(
            "/auth/verifysms",
            new VerifyMemberOtpRequest(phone, code!));
        var tokens = await verifyResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.Data!.AccessToken);

        await using var jpeg = new MemoryStream();
        using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(32, 32))
        {
            await image.SaveAsJpegAsync(jpeg);
        }

        jpeg.Position = 0;
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(jpeg.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "photos", "face.jpg");

        var uploadResponse = await client.PostAsync("/users/me/photos", form);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        Assert.True(
            uploadResponse.StatusCode == HttpStatusCode.OK,
            $"Upload failed: {uploadResponse.StatusCode} {uploadBody}");
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<List<MemberPhotoDto>>>(JsonOptions);
        Assert.NotNull(uploaded?.Data);
        Assert.Single(uploaded!.Data!);
        Assert.True(uploaded.Data[0].IsReference);

        var listResponse = await client.GetAsync("/users/me/photos");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = await listResponse.Content.ReadFromJsonAsync<ApiResponse<List<MemberPhotoDto>>>(JsonOptions);
        Assert.Single(listed!.Data!);

        var photoId = listed.Data![0].Id;
        var bytesResponse = await client.GetAsync($"/users/me/photos/{photoId}");
        Assert.Equal(HttpStatusCode.OK, bytesResponse.StatusCode);
        Assert.Equal("image/jpeg", bytesResponse.Content.Headers.ContentType?.MediaType);
        var bytes = await bytesResponse.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);

        var deleteResponse = await client.DeleteAsync($"/users/me/photos/{photoId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var emptyList = await client.GetAsync("/users/me/photos");
        var afterDelete = await emptyList.Content.ReadFromJsonAsync<ApiResponse<List<MemberPhotoDto>>>(JsonOptions);
        Assert.Empty(afterDelete!.Data!);
    }

    [Fact]
    public async Task RegisterLoginVerifyEmailOtp_ConfirmsEmail()
    {
        var client = _factory.CreateClient();
        var phone = "9000111222";
        var email = "otp.email@example.com";

        var registerResponse = await client.PostAsJsonAsync(
            "/users/register",
            RegisterBody(phone, email));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/auth/login",
            new RequestMemberOtpRequest(email));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var code = _factory.Email.GetOtpCode(email);
        Assert.False(string.IsNullOrWhiteSpace(code));

        var verifyResponse = await client.PostAsJsonAsync(
            "/auth/verifysms",
            new VerifyMemberOtpRequest(email, code!));
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var tokens = await verifyResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        Assert.True(tokens!.Data!.Account.EmailConfirmed);
        Assert.False(tokens.Data.Account.PhoneConfirmed);
    }

    [Fact]
    public async Task RegisterWithPassword_ThenPasswordLogin()
    {
        var client = _factory.CreateClient();
        var phone = "9000222333";

        var registerResponse = await client.PostAsJsonAsync(
            "/users/register",
            RegisterBody(phone, "pwd.login@example.com", "secret12"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/auth/password",
            new MemberPasswordLoginRequest(phone, "secret12"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var tokens = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        Assert.False(string.IsNullOrWhiteSpace(tokens!.Data!.AccessToken));
        Assert.Equal("+919000222333", tokens.Data.Account.Phone);
    }

    [Fact]
    public async Task ForgotPassword_Reset_ThenPasswordLogin()
    {
        var client = _factory.CreateClient();
        var phone = "9000333444";

        var registerResponse = await client.PostAsJsonAsync(
            "/users/register",
            RegisterBody(phone, "pwd.reset@example.com", "oldpass12"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var forgotResponse = await client.PostAsJsonAsync(
            "/auth/password/forgot",
            new ForgotMemberPasswordRequest(phone));
        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);

        var code = _factory.Sms.GetCode("+919000333444");
        Assert.False(string.IsNullOrWhiteSpace(code));

        var resetResponse = await client.PostAsJsonAsync(
            "/auth/password/reset",
            new ResetMemberPasswordRequest(phone, code!, "newpass12"));
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/auth/password",
            new MemberPasswordLoginRequest(phone, "newpass12"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task AdminPasswordLogin_IssuesAdminTokens()
    {
        var email = "ops.admin@example.com";
        var phone = "+919000555666";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            if (await users.FindByEmailAsync(email, CancellationToken.None) is null)
            {
                await users.CreateAdminAsync(email, phone, "adminpass12", CancellationToken.None);
            }
        }

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/admin/password",
            new AdminPasswordLoginRequest(email, "adminpass12"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var tokens = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        Assert.Equal("Admin", tokens!.Data!.Account.AccountKind);
        Assert.Contains("admin", tokens.Data.Account.Roles);
        Assert.False(tokens.Data.Account.IsSuperAdmin);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.Data.AccessToken);

        var meResponse = await client.GetAsync("/admin/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.Equal("Admin", me!.Data!.AccountKind);
        Assert.Equal(email, me.Data.Email);
        Assert.False(me.Data.IsSuperAdmin);
        Assert.Null(me.Data.Profile);
    }

    [Fact]
    public async Task SuperAdmin_CanCreateAnotherAdmin()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var superEmail = $"super.{suffix}@example.com";
        var phoneStamp = Math.Abs(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0)) % 100_000_000;
        var superPhone = $"+9198{phoneStamp:D8}";
        var createdEmail = $"created.{suffix}@example.com";

        Guid superId;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var super = await users.CreateAdminAsync(
                superEmail,
                superPhone,
                "adminpass12",
                CancellationToken.None);
            superId = super.Id;
        }

        await MarkSuperAdminAsync(superId);

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/admin/password",
            new AdminPasswordLoginRequest(superEmail, "adminpass12"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var tokens = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.Data!.AccessToken);

        var createResponse = await client.PostAsJsonAsync(
            "/admin/admins",
            new CreateAdminRequest(createdEmail, "created12"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.Equal("Admin", created!.Data!.AccountKind);
        Assert.False(created.Data.IsSuperAdmin);
        Assert.Equal(createdEmail, created.Data.Email);
    }

    private async Task MarkSuperAdminAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ElarisDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.IsSuperAdmin = true;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AdminOtp_RejectsRegisteredMember()
    {
        var client = _factory.CreateClient();
        var phone = "9000666777";
        var registerResponse = await client.PostAsJsonAsync(
            "/users/register",
            RegisterBody(phone, "member.not.admin@example.com"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/admin/otp/request",
            new RequestAdminOtpRequest(phone));
        Assert.Equal(HttpStatusCode.NotFound, loginResponse.StatusCode);

        var fail = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<object?>>(JsonOptions);
        Assert.Equal("user_not_found", fail!.ErrorCode);
    }
}
