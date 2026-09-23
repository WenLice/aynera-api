using Aynera.Infrastructure;
using Aynera.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Application.Features.Auth.Services.Interfaces;
using Aynera.Domain.Auth.Requests;
using Aynera.Domain.Auth.Responses;
using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Common;
using Aynera.Domain.Photos.Responses;
using Aynera.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SixLabors.ImageSharp;

namespace Aynera.Api.IntegrationTests;

[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<AuthApiFactory>, ICollectionFixture<RedisOtpAuthApiFactory>;

/// <summary>
/// Shared API factory for integration tests.
/// Uses Postgres database <c>aynera_test</c> (created by docker init or on first run when permitted).
/// </summary>
public class AuthApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly bool _useInMemoryOtpStore;

    public CapturingSmsService Sms { get; } = new();
    public CapturingEmailService Email { get; } = new();

    public AuthApiFactory() : this(useInMemoryOtpStore: true)
    {
    }

    internal AuthApiFactory(bool useInMemoryOtpStore)
    {
        _useInMemoryOtpStore = useInMemoryOtpStore;
        var baseConnection =
            Environment.GetEnvironmentVariable("AYNERA_TEST_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("AYNERA_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Username=aynera;Password=Admin@1";

        const string databaseName = "aynera_test";
        EnsureDatabaseExists(baseConnection, databaseName);
        _connectionString = BuildDatabaseConnectionString(baseConnection, databaseName);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Aynera:Jwt:SigningKey", "test-signing-key-that-is-long-enough-32");
        builder.UseSetting("Aynera:Jwt:Issuer", "aynera-api");
        builder.UseSetting("AYNERA_JWT_SIGNING_KEY", "test-signing-key-that-is-long-enough-32");
        builder.UseSetting("ConnectionStrings:Aynera", _connectionString);
        builder.UseSetting("AYNERA_DB_CONNECTION", _connectionString);
        builder.UseSetting("Aynera:Email:VerifyLinkBaseUrl", "http://localhost:5173/verify-email");
        builder.UseSetting("Aynera:UseInMemoryOtpStore", _useInMemoryOtpStore ? "true" : "false");

        builder.ConfigureServices(services =>
        {
            // Dispatch explicitly in tests so delivery is deterministic while using the production dispatcher.
            var worker = services.Single(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(VerificationEmailHostedService));
            services.Remove(worker);
            var venueWorker = services.Single(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(VenueNotificationHostedService));
            services.Remove(venueWorker);
            services.RemoveAll<ISmsService>();
            services.AddSingleton<ISmsService>(Sms);
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Email);
        });
    }

    public async Task<string> DeliverVerificationAsync(string address)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<VerificationEmailDispatcher>().DispatchPendingAsync(CancellationToken.None);
            if (Email.GetVerificationLink(address) is { } link) return link;
        }
        throw new InvalidOperationException("Verification email was not delivered.");
    }

    private static void EnsureDatabaseExists(string baseConnection, string databaseName)
    {
        var adminConnection = BuildDatabaseConnectionString(baseConnection, "aynera");
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
            "Ada Lovelace",
            Gender.Female,
            new DateOnly(1990, 5, 15),
            "Bangalore",
            email,
            Hometown: "Pune",
            Religion: "Hindu",
            Password: password);

    [Fact]
    public async Task RegisterLoginVerifySmsMe_RoundTrip()
    {
        var client = _factory.CreateClient();
        var phone = "9988776655";

        var registerResponse = await client.PostAsJsonAsync(
            "/members/register",
            RegisterBody(phone, "roundtrip@example.com"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/auth/otp/request",
            new RequestMemberOtpRequest(phone));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var code = _factory.Sms.GetCode("+919988776655");
        Assert.False(string.IsNullOrWhiteSpace(code));

        var verifyResponse = await client.PostAsJsonAsync(
            "/auth/otp/verify",
            new VerifyMemberOtpRequest(phone, code!));
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var tokensEnvelope = await verifyResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        Assert.NotNull(tokensEnvelope);
        Assert.True(tokensEnvelope!.Success);
        Assert.NotNull(tokensEnvelope.Data);
        Assert.False(string.IsNullOrWhiteSpace(tokensEnvelope.Data!.AccessToken));
        Assert.NotNull(tokensEnvelope.Data.Account.Profile);
        Assert.Equal("Ada Lovelace", tokensEnvelope.Data.Account.Profile!.Name);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokensEnvelope.Data.AccessToken);

        var meResponse = await client.GetAsync("/members/me");
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
            "/auth/otp/request",
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
            "/members/register",
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
        Assert.Equal("Ada Lovelace", envelope.Data.Profile!.Name);
        Assert.Equal("Female", envelope.Data.Profile.Gender);
        Assert.Equal("Bangalore", envelope.Data.Profile.City);
        Assert.False(envelope.Data.EmailConfirmed);
        Assert.False(string.IsNullOrWhiteSpace(await _factory.DeliverVerificationAsync("new.member@example.com")));

        var duplicate = await client.PostAsJsonAsync(
            "/members/register",
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
            "/members/register",
            RegisterBody(phone, "confirm.email@example.com"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.NotNull(created?.Data);
        Assert.False(created!.Data!.EmailConfirmed);

        var verifyUrl = await _factory.DeliverVerificationAsync("confirm.email@example.com");
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
            "Kid User",
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16)),
            "Bangalore",
            "kid@example.com",
            Hometown: "Pune");

        var response = await client.PostAsJsonAsync("/members/register", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var fail = await response.Content.ReadFromJsonAsync<ApiResponse<object?>>(JsonOptions);
        Assert.NotNull(fail);
        Assert.False(fail!.Success);
        Assert.Equal("underage", fail.ErrorCode);
    }

    [Fact]
    public async Task Register_ResolvesCityToCatalogKey_CaseInsensitively()
    {
        var client = _factory.CreateClient();
        var phone = "7" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var body = RegisterBody(phone, $"city-{Guid.NewGuid():N}@example.com") with { City = "  bangalore " };

        var response = await client.PostAsJsonAsync("/members/register", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        var profile = created!.Data!.Profile!;
        Assert.Equal("Bangalore", profile.City);
        Assert.NotEqual(Guid.Empty, profile.CityId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        var city = await db.EarlyAccessCities.SingleAsync(c => c.Name == "Bangalore");
        Assert.Equal(city.Id, profile.CityId);
        Assert.Equal(city.Id, (await db.MemberProfiles.SingleAsync(p => p.UserId == created.Data.Id)).CityId);
    }

    [Fact]
    public async Task Register_UnknownCity_ReturnsCityNotSupported()
    {
        var client = _factory.CreateClient();
        var phone = "7" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var body = RegisterBody(phone, $"city-{Guid.NewGuid():N}@example.com") with { City = "Hyderabad" };

        var response = await client.PostAsJsonAsync("/members/register", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var fail = await response.Content.ReadFromJsonAsync<ApiResponse<object?>>(JsonOptions);
        Assert.Equal("city_not_supported", fail!.ErrorCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.PhoneNumber == "+91" + phone));
    }

    [Fact]
    public async Task DeleteAccount_ThenReregister_CreatesNewAccount()
    {
        var client = _factory.CreateClient();
        var phone = "6655443322";

        var createResponse = await client.PostAsJsonAsync(
            "/members/register",
            RegisterBody(phone));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.NotNull(created?.Data);

        var loginResponse = await client.PostAsJsonAsync(
            "/auth/otp/request",
            new RequestMemberOtpRequest(phone));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var code = _factory.Sms.GetCode("+916655443322");
        var verifyResponse = await client.PostAsJsonAsync(
            "/auth/otp/verify",
            new VerifyMemberOtpRequest(phone, code!));
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var tokens = await verifyResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.Data!.AccessToken);

        var deleteResponse = await client.DeleteAsync("/members/me");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;

        var reregister = await client.PostAsJsonAsync(
            "/members/register",
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
            "/auth/otp/request",
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

        await client.PostAsJsonAsync("/members/register", RegisterBody(phone, "photos@example.com"));

        var loginResponse = await client.PostAsJsonAsync("/auth/otp/request", new RequestMemberOtpRequest(phone));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var code = _factory.Sms.GetCode("+919333444555");
        var verifyResponse = await client.PostAsJsonAsync(
            "/auth/otp/verify",
            new VerifyMemberOtpRequest(phone, code!));
        var tokens = await verifyResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.Data!.AccessToken);
        await FaceCheck.PassAsync(client);

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

        var uploadResponse = await client.PostAsync("/photos/Upload", form);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        Assert.True(
            uploadResponse.StatusCode == HttpStatusCode.OK,
            $"Upload failed: {uploadResponse.StatusCode} {uploadBody}");
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<List<MemberPhotoDto>>>(JsonOptions);
        Assert.NotNull(uploaded?.Data);
        Assert.Single(uploaded!.Data!);
        Assert.True(uploaded.Data[0].IsReference);

        var listResponse = await client.GetAsync("/photos/GetAll");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = await listResponse.Content.ReadFromJsonAsync<ApiResponse<List<MemberPhotoDto>>>(JsonOptions);
        Assert.Single(listed!.Data!);

        var photoId = listed.Data![0].Id;
        var bytesResponse = await client.GetAsync($"/photos/{photoId}");
        Assert.Equal(HttpStatusCode.OK, bytesResponse.StatusCode);
        Assert.Equal("image/jpeg", bytesResponse.Content.Headers.ContentType?.MediaType);
        var bytes = await bytesResponse.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);

        var deleteResponse = await client.DeleteAsync($"/photos/{photoId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var emptyList = await client.GetAsync("/photos/GetAll");
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
            "/members/register",
            RegisterBody(phone, email));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/auth/otp/request",
            new RequestMemberOtpRequest(email));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var code = _factory.Email.GetOtpCode(email);
        Assert.False(string.IsNullOrWhiteSpace(code));

        var verifyResponse = await client.PostAsJsonAsync(
            "/auth/otp/verify",
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
            "/members/register",
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
            "/members/register",
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
        const string loginPath = "/auth/admin/password";
        const string mePath = "/admins/me";
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
            loginPath,
            new AdminPasswordLoginRequest(email, "adminpass12"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var tokens = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        Assert.Equal("Admin", tokens!.Data!.Account.AccountKind);
        Assert.Contains("admin", tokens.Data.Account.Roles);
        Assert.False(tokens.Data.Account.IsSuperAdmin);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.Data.AccessToken);

        var meResponse = await client.GetAsync(mePath);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<ApiResponse<AuthAccountDto>>(JsonOptions);
        Assert.Equal("Admin", me!.Data!.AccountKind);
        Assert.Equal(email, me.Data.Email);
        Assert.False(me.Data.IsSuperAdmin);
        Assert.Null(me.Data.Profile);
        foreach (var path in new[] { "/members/GetAll?page=1&pageSize=15", "/suggestions/GetAll?page=1", "/feedback/GetAll?page=1", "/audit/events/GetAll?page=1" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task SuperAdmin_CanCreateAnotherAdmin()
    {
        const string loginPath = "/auth/admin/password";
        const string createPath = "/admins/Create";
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
            loginPath,
            new AdminPasswordLoginRequest(superEmail, "adminpass12"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var tokens = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<TokenResponse>>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.Data!.AccessToken);

        var createResponse = await client.PostAsJsonAsync(
            createPath,
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
        var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
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
            "/members/register",
            RegisterBody(phone, "member.not.admin@example.com"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/auth/admin/otp/request",
            new RequestAdminOtpRequest(phone));
        Assert.Equal(HttpStatusCode.NotFound, loginResponse.StatusCode);

        var fail = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<object?>>(JsonOptions);
        Assert.Equal("user_not_found", fail!.ErrorCode);
    }
}
