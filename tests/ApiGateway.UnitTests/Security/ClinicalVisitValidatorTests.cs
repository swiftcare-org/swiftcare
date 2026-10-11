using System.Net;
using System.Text.Json;
using ApiGateway.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.UnitTests.Security;

public sealed class ClinicalVisitValidatorTests
{
    private static readonly Guid Doctor = Guid.NewGuid(), Patient = Guid.NewGuid(), Queue = Guid.NewGuid(), Consultation = Guid.NewGuid();

    private static ClinicalVisitValidator Create(Handler handler, bool emptyDestination = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gateway:InternalSecret"] = "trusted-secret" }).Build();
        return new(new HttpClient(handler), new Proxy(emptyDestination), configuration);
    }

    private static string Visit(string status, Guid? doctor = null, Guid? patient = null, Guid? queue = null) =>
        JsonSerializer.Serialize(new { DoctorId = doctor ?? Doctor, PatientId = patient ?? Patient, QueueId = queue ?? Queue, Status = status });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidVisitChecksItsAuthorityAndPatientExistence(bool outcome)
    {
        using var handler = new Handler(Visit(outcome ? "COMPLETE" : "IN_CONSULTATION"));
        Assert.True(await Create(handler).ValidateAsync(Doctor, Patient, Queue, outcome ? Consultation : null, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal($"http://{(outcome ? "medical-record" : "queue")}.test/internal/visits/{(outcome ? Consultation : Queue)}", handler.Requests[0].Url);
        Assert.Equal($"http://patient.test/api/patients/{Patient}", handler.Requests[1].Url);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("trusted-secret", request.Secret);
            Assert.Equal("Doctor", request.Role);
            Assert.Equal(Doctor.ToString(), request.UserId);
        });
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("patient")]
    [InlineData("queue")]
    [InlineData("incomplete")]
    [InlineData("wrongStatusCase")]
    [InlineData("null")]
    public async Task InvalidCompletedVisitCannotReachTheSaveEndpoint(string mismatch)
    {
        using var handler = new Handler(mismatch == "null" ? "null" : Visit(
            mismatch == "incomplete" ? "IN_PROGRESS" : mismatch == "wrongStatusCase" ? "complete" : "COMPLETE",
            mismatch == "doctor" ? Guid.NewGuid() : Doctor,
            mismatch == "patient" ? Guid.NewGuid() : Patient,
            mismatch == "queue" ? Guid.NewGuid() : Queue));
        Assert.False(await Create(handler).ValidateAsync(Doctor, Patient, Queue, Consultation, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task WaitingQueueEntryIsIneligible()
    {
        using var handler = new Handler(Visit("INELIGIBLE"));
        Assert.False(await Create(handler).ValidateAsync(Doctor, Patient, Queue, null, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingVisitOrPatientIsRejected(bool visitMissing)
    {
        using var handler = new Handler(Visit("COMPLETE"), visitMissing ? HttpStatusCode.NotFound : HttpStatusCode.OK,
            visitMissing ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        Assert.False(await Create(handler).ValidateAsync(Doctor, Patient, Queue, Consultation, CancellationToken.None));
        Assert.Equal(visitMissing ? 1 : 2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthorityFailuresArePropagated(bool visitFailure)
    {
        using var handler = new Handler(Visit("COMPLETE"), visitFailure ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
            visitFailure ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<HttpRequestException>(() => Create(handler).ValidateAsync(Doctor, Patient, Queue, Consultation, CancellationToken.None));
    }

    [Fact]
    public async Task MalformedAuthorityPayloadIsPropagated()
    {
        using var handler = new Handler("invalid");
        await Assert.ThrowsAsync<JsonException>(() => Create(handler).ValidateAsync(Doctor, Patient, Queue, Consultation, CancellationToken.None));
    }

    [Fact]
    public async Task MissingDestinationFailsClosed()
    {
        using var handler = new Handler(Visit("COMPLETE"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(handler, emptyDestination: true)
            .ValidateAsync(Doctor, Patient, Queue, Consultation, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    private sealed class Handler(string body, HttpStatusCode visitStatus = HttpStatusCode.OK, HttpStatusCode patientStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Url, string Secret, string Role, string UserId)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Headers.GetValues("X-Gateway-Secret").Single(),
                request.Headers.GetValues("X-User-Role").Single(), request.Headers.GetValues("X-User-Id").Single()));
            return Task.FromResult(new HttpResponseMessage(Requests.Count == 1 ? visitStatus : patientStatus)
            {
                Content = new StringContent(Requests.Count == 1 ? body : "{}")
            });
        }
    }

    private sealed class Proxy(bool emptyDestination) : IProxyConfigProvider
    {
        public IProxyConfig GetConfig() => new Config(emptyDestination);
    }

    private sealed class Config(bool emptyDestination) : IProxyConfig
    {
        public IReadOnlyList<RouteConfig> Routes => [];
        public IReadOnlyList<ClusterConfig> Clusters => new[] { "queue", "medical-record", "patient" }.Select(name => new ClusterConfig
        {
            ClusterId = name + "-cluster",
            Destinations = emptyDestination ? null : new Dictionary<string, DestinationConfig> { ["destination"] = new() { Address = $"http://{name}.test/" } }
        }).ToList();
        public IChangeToken ChangeToken => new CancellationChangeToken(CancellationToken.None);
    }
}
