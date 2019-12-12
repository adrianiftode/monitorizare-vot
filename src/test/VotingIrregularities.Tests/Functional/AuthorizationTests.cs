using FluentAssertions;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Threading.Tasks;
using VotingIrregularities.Tests.Functional.Fixtures;
using Xunit;

namespace VotingIrregularities.Tests.Functional
{
    public class AuthorizationTests : IClassFixture<WebApiApplicationFactory>
    {
        private readonly HttpClient _client;
        public AuthorizationTests(WebApiApplicationFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task GivenAuthorizeRequest_WhenCredentialsAreNotValid_ThenReturnsBadRequestWithALoginError()
        {
            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/access/authorize", new
            {
                user = "some user",
                password = "some password",
                uniqueId = "some unique id"
            });

            // Assert
            response.Should().Be400BadRequest()
                .And.HaveError("error", "A aparut o eroare la logarea in aplicatie*");
        }

        [Fact]
        public async Task GivenAuthorizeRequest_WhenHasNotBody_ThenReturnsBadRequestWithRequiredData()
        {
            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/access/authorize", new
            {
            });

            // Assert
            response.Should().Be400BadRequest()
                .And.HaveError("user", "*required*")
                .And.HaveError("password", "*required*")
                ;
        }

        [Fact]
        public async Task GivenAuthorizeRequest_WhenCredentialsAreValid_ThenReturnsAccessToken()
        {
            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/access/authorize", new
            {
                user = "0722222222",
                password = "1234",
                uniqueId = "some unique id"
            });

            // Assert
            response.Should().Be200Ok()
                .And.Satisfy(givenModelStructure:
                    new
                    {
                        access_token = default(string),
                        expires_in = default(int)
                    }, assertion: model =>
                    {
                        model.access_token.Should().NotBeNullOrEmpty();
                        model.expires_in.Should().Be(86400);

                        var handler = new JwtSecurityTokenHandler();
                        var token = handler.ReadToken(model.access_token) as JwtSecurityToken;

                        token?.Subject.Should().Be("0722222222");
                    });
        }
    }
}
