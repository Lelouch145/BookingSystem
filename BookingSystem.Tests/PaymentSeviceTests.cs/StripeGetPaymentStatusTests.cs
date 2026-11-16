using System.Net;
using System.Runtime.InteropServices.Marshalling;
using BookingSystem.Api.Database;
using BookingSystem.Api.Models.SystemModels;
using BookingSystem.Api.Services;
using BookingSystem.Api.Services.interfaces;
using BookingSystem.Api.Services.PaymentProviders;
using Castle.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Stripe;

namespace BookingSystem.Tests;

public class StripeGetPaymentStatusTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _databaseFixture;

    public StripeGetPaymentStatusTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    public async Task InitializeAsync()
    {
        await _databaseFixture.ResetDatabaseAsync();
    }
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetPaymentStatus_EmptySessionId()
    {
        var stripeClient = new StripeClient();
        var logger = NullLogger<StripePaymentProvider>.Instance;

        var stripeService = new StripePaymentProvider(stripeClient, logger);

        var status = await stripeService.GetPaymentStatusAsync("");

        Assert.Equal(Error.SessionIdIsEmpty, status.ErrorMessage);
    }
    [Fact]
    public async Task GetPaymentStatus_StripeFails()
    {
        var mockHttp = new Mock<HttpMessageHandler>();
        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.BadRequest,
            Content = new StringContent(
                """
                {
                    "error": {
                        "type": "invalid_request_error",
                        "message": "Simulated Stripe error"
                    }
                }
            """)

        });

        using var httpClient = new HttpClient(mockHttp.Object);

        var stripeClient = new StripeClient("sk_test_fake", httpClient: new SystemNetHttpClient(httpClient));
        var logger = NullLogger<StripePaymentProvider>.Instance;

        var stripeService = new StripePaymentProvider(stripeClient, logger);

        var status = await stripeService.GetPaymentStatusAsync("test");

        Assert.Equal(Error.StripeServiceUnavailable, status.ErrorMessage);
    }

    [Fact]
    public async Task GetpaymentStatus_StatusIsNull()
    {
        var logger = NullLogger<StripePaymentProvider>.Instance;
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    """
                    {
                        "id": "cs_test_123",
                        "object": "checkout.session",
                        "status": null,
                        "payment_status": null
                    }
                    """)
            });
        
        using var httpClient = new HttpClient(handlerMock.Object);

        var stripeClient = new StripeClient("sk_test_fake", httpClient: new SystemNetHttpClient(httpClient));
        var stripeService = new StripePaymentProvider(stripeClient, logger);

        var status = await stripeService.GetPaymentStatusAsync("test");

        Assert.Equal(Error.StripeStatusIsNull, status.ErrorMessage);
    }
    [Fact]
    public async Task GetPaymetnStatus_Succeeds()
    {
        var logger = NullLogger<StripePaymentProvider>.Instance;
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    """
                    {
                        "id": "cs_test_123",
                        "object": "checkout.session",
                        "status": "complete",
                        "payment_status": "paid",
                        "payment_intent": "pi_test_123",
                        "client_reference_id": "123"
                    }
                    """)
            });
        using var httpClient = new HttpClient(handlerMock.Object);

        var stripeClient = new StripeClient("sk_test_fake", httpClient: new SystemNetHttpClient(httpClient));

        var stripeService = new StripePaymentProvider(stripeClient, logger);

        var status = await stripeService.GetPaymentStatusAsync("test"); 

        Assert.Equal("cs_test_123", status.SessionId);
    }


}