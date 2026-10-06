using QueueService.Models.Configuration;

namespace QueueService.UnitTests.Configuration;

public class KafkaOptionsTests
{
    [Fact]
    public void ConsultationCompletedDefaultsMatchTheTopicMedicalRecordServicePublishesTo()
    {
        var options = new KafkaOptions
        {
            BootstrapServers = "localhost:9092",
            PatientCheckedInTopic = "patient-checked-in",
            PatientCalledTopic = "patient-called",
            ConsumerGroupId = "queue-service"
        };

        Assert.Equal("consultation-completed", options.ConsultationCompletedTopic);
        Assert.Equal("queue-service-consultations", options.ConsultationCompletedConsumerGroup);
    }
}
