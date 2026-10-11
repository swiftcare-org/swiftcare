using System.Net;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway.Security;

public sealed class ClinicalVisitValidator(HttpClient client, IProxyConfigProvider proxy, IConfiguration configuration)
    : IClinicalVisitValidator
{
    public async Task<bool> ValidateAsync(Guid doctorId, Guid patientId, Guid queueId, Guid? consultationId,
        CancellationToken cancellationToken)
    {
        var cluster = consultationId.HasValue ? "medical-record-cluster" : "queue-cluster";
        var visitId = consultationId ?? queueId;
        using var visitResponse = await SendAsync(cluster, $"/internal/visits/{visitId}", doctorId, cancellationToken);
        if (visitResponse.StatusCode == HttpStatusCode.NotFound) return false;
        visitResponse.EnsureSuccessStatusCode();
        var visit = await visitResponse.Content.ReadFromJsonAsync<VisitContext>(cancellationToken);
        if (visit is null || visit.DoctorId != doctorId || visit.PatientId != patientId || visit.QueueId != queueId
            || visit.Status != (consultationId.HasValue ? "COMPLETE" : "IN_CONSULTATION"))
            return false;
        using var patientResponse = await SendAsync("patient-cluster", $"/api/patients/{patientId}", doctorId, cancellationToken);
        if (patientResponse.StatusCode == HttpStatusCode.NotFound) return false;
        patientResponse.EnsureSuccessStatusCode();
        return true;
    }

    private Task<HttpResponseMessage> SendAsync(string clusterId, string path, Guid doctorId, CancellationToken cancellationToken)
    {
        var cluster = proxy.GetConfig().Clusters.Single(cluster => cluster.ClusterId == clusterId);
        var address = cluster.Destinations?.Values.FirstOrDefault()?.Address
            ?? throw new InvalidOperationException("Clinical validation destination is unavailable.");
        var request = new HttpRequestMessage(HttpMethod.Get, address.TrimEnd('/') + path);
        request.Headers.Add("X-Gateway-Secret", configuration["Gateway:InternalSecret"]);
        request.Headers.Add("X-User-Role", "Doctor");
        request.Headers.Add("X-User-Id", doctorId.ToString());
        return SendAndDisposeAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAndDisposeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            return await client.SendAsync(request, cancellationToken);
        }
    }

    private sealed record VisitContext(Guid PatientId, Guid QueueId, Guid? DoctorId, string Status);
}
