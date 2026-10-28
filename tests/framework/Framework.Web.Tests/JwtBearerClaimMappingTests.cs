/********************************************************************************
 * Copyright (c) 2026 Contributors to the Eclipse Foundation
 *
 * See the NOTICE file(s) distributed with this work for additional
 * information regarding copyright ownership.
 *
 * This program and the accompanying materials are made available under the
 * terms of the Apache License, Version 2.0 which is available at
 * https://www.apache.org/licenses/LICENSE-2.0.
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 * SPDX-License-Identifier: Apache-2.0
 ********************************************************************************/

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Org.Eclipse.TractusX.Portal.Backend.Framework.Web.Tests;

/// <summary>
/// Runs real signed tokens through the JwtBearer options registered by
/// <see cref="StartupServiceExtensions.AddDefaultServices{TProgram}"/>, so a
/// regression in inbound claim mapping (e.g. `sub` turned into
/// <see cref="ClaimTypes.NameIdentifier"/>) is caught.
/// </summary>
public class JwtBearerClaimMappingTests
{
    private const string Issuer = "https://central.example.org/auth/realms/CX-Central";
    private const string Audience = "Cl2-CX-Portal";
    private const string Subject = "6b0c8a3e-1d0f-4b3c-9f0e-2a7d5c4b1e90";

    private readonly SymmetricSecurityKey _signingKey = new(RandomNumberGenerator.GetBytes(32));
    private readonly JwtBearerOptions _options;

    public JwtBearerClaimMappingTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtBearerOptions:RequireHttpsMetadata"] = "true",
                ["JwtBearerOptions:MetadataAddress"] = $"{Issuer}/.well-known/openid-configuration",
                ["JwtBearerOptions:TokenValidationParameters:ValidateIssuer"] = "true",
                ["JwtBearerOptions:TokenValidationParameters:ValidIssuer"] = Issuer,
                ["JwtBearerOptions:TokenValidationParameters:ValidateIssuerSigningKey"] = "true",
                ["JwtBearerOptions:TokenValidationParameters:ValidateAudience"] = "true",
                ["JwtBearerOptions:TokenValidationParameters:ValidAudience"] = Audience,
                ["JwtBearerOptions:TokenValidationParameters:ValidateLifetime"] = "true",
                ["JwtBearerOptions:TokenValidationParameters:ClockSkew"] = "00:00:00",
            })
            .Build();

        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddDefaultServices<JwtBearerClaimMappingTests>(configuration, "v1", "test", false)
            .BuildServiceProvider();

        _options = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public async Task ValidateToken_WithSubAndNonGuidUserName_KeepsOidcClaimNames()
    {
        // Arrange
        var token = CreateToken(_signingKey, Issuer, Audience, DateTime.UtcNow.AddMinutes(5));

        // Act
        var result = await ValidateAsync(token);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ClaimsIdentity.Claims.Should().ContainSingle(c => c.Type == "sub" && c.Value == Subject);
        result.ClaimsIdentity.Claims.Should().NotContain(c => c.Type == ClaimTypes.NameIdentifier);
        result.ClaimsIdentity.Claims.Should().ContainSingle(c => c.Type == "preferred_username" && c.Value == "jane.doe@shared-idp");
    }

    [Fact]
    public async Task ValidateToken_WithForeignSigningKey_IsRejected()
    {
        var token = CreateToken(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), Issuer, Audience, DateTime.UtcNow.AddMinutes(5));

        var result = await ValidateAsync(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToken_WithWrongAudience_IsRejected()
    {
        var token = CreateToken(_signingKey, Issuer, "other-client", DateTime.UtcNow.AddMinutes(5));

        var result = await ValidateAsync(token);

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<SecurityTokenInvalidAudienceException>();
    }

    [Fact]
    public async Task ValidateToken_WithWrongIssuer_IsRejected()
    {
        var token = CreateToken(_signingKey, "https://shared-idp.example.org/realms/other", Audience, DateTime.UtcNow.AddMinutes(5));

        var result = await ValidateAsync(token);

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public async Task ValidateToken_Expired_IsRejected()
    {
        var token = CreateToken(_signingKey, Issuer, Audience, DateTime.UtcNow.AddMinutes(-1));

        var result = await ValidateAsync(token);

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<SecurityTokenExpiredException>();
    }

    private Task<TokenValidationResult> ValidateAsync(string token)
    {
        // Use the registered parameters and only replace the remote key source
        // (Keycloak JWKS) with the local test key.
        var parameters = _options.TokenValidationParameters.Clone();
        parameters.ConfigurationManager = null;
        parameters.IssuerSigningKey = _signingKey;
        return _options.TokenHandlers.OfType<JsonWebTokenHandler>().Single().ValidateTokenAsync(token, parameters);
    }

    private static string CreateToken(SecurityKey key, string issuer, string audience, DateTime expires) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = expires.AddMinutes(-10),
            IssuedAt = expires.AddMinutes(-10),
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Subject,
                ["preferred_username"] = "jane.doe@shared-idp",
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
}
