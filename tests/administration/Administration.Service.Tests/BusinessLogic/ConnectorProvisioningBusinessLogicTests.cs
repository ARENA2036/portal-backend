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

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.Administration.Service.BusinessLogic;
using Org.Eclipse.TractusX.Portal.Backend.Administration.Service.Controllers;
using Org.Eclipse.TractusX.Portal.Backend.Administration.Service.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Administration.Service.Models;
using Org.Eclipse.TractusX.Portal.Backend.Framework.DateTimeProvider;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Identity;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Models;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Entities;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Web.Identity;
using System.Reflection;
using System.Security.Claims;

namespace Org.Eclipse.TractusX.Portal.Backend.Administration.Service.Tests.BusinessLogic;

public class ConnectorProvisioningBusinessLogicTests
{
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid ApplicationId = Guid.NewGuid();
    private static readonly Guid HostCompanyId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid Version = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private const string Bpn = "BPNL0000000000ZZ";
    private const string ConnectorUrl = "http://edc-bpnl0000000000zz-controlplane.be293-connectors.svc.cluster.local:8084/api/v1/dsp";

    private readonly IPortalRepositories _portalRepositories;
    private readonly IConnectorProvisioningRequestRepository _requestRepository;
    private readonly IConnectorsRepository _connectorsRepository;
    private readonly IIdentityService _identityService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ConnectorProvisioningSettings _settings;
    private readonly ConnectorProvisioningBusinessLogic _sut;

    public ConnectorProvisioningBusinessLogicTests()
    {
        _portalRepositories = A.Fake<IPortalRepositories>();
        _requestRepository = A.Fake<IConnectorProvisioningRequestRepository>();
        _connectorsRepository = A.Fake<IConnectorsRepository>();
        _identityService = A.Fake<IIdentityService>();
        _dateTimeProvider = A.Fake<IDateTimeProvider>();
        var identity = A.Fake<IIdentityData>();
        A.CallTo(() => identity.IdentityId).Returns(OperatorId);
        A.CallTo(() => _identityService.IdentityData).Returns(identity);
        A.CallTo(() => _dateTimeProvider.OffsetNow).Returns(Now);
        A.CallTo(() => _portalRepositories.GetInstance<IConnectorProvisioningRequestRepository>()).Returns(_requestRepository);
        A.CallTo(() => _portalRepositories.GetInstance<IConnectorsRepository>()).Returns(_connectorsRepository);
        _settings = new ConnectorProvisioningSettings { DevOperatorRequestsEnabled = false, HostCompanyId = HostCompanyId, ConnectorLocationId = "DE" };
        _sut = new ConnectorProvisioningBusinessLogic(_portalRepositories, _identityService, _dateTimeProvider, Options.Create(_settings));
    }

    private void Current(ConnectorProvisioningRequestStatusId status, Guid? connectorId = null) =>
        A.CallTo(() => _requestRepository.GetRequestStatusData(RequestId))
            .Returns(new ConnectorProvisioningRequestStatusData(status, CompanyId, Bpn, connectorId, Version));

    private ConnectorProvisioningRequest CaptureModification()
    {
        var request = new ConnectorProvisioningRequest(RequestId);
        A.CallTo(() => _requestRepository.AttachAndModifyRequest(RequestId, Version, A<Action<ConnectorProvisioningRequest>>._))
            .Invokes((Guid _, Guid _, Action<ConnectorProvisioningRequest> modify) => modify(request));
        return request;
    }

    #region GetRequests

    [Fact]
    public async Task GetRequests_MapsTheControllerContract()
    {
        A.CallTo(() => _requestRepository.GetRequestsByStatus(ConnectorProvisioningRequestStatusId.REQUESTED))
            .Returns(new[]
            {
                new ConnectorProvisioningRequestData(RequestId, CompanyId, ApplicationId, Bpn, ConnectorProvisioningBasisId.DEV_OPERATOR_APPROVED,
                    CompanyApplicationStatusId.SUBMITTED, CompanyStatusId.PENDING, OperatorId)
            }.ToAsyncEnumerable());

        var result = await _sut.GetRequests(ConnectorProvisioningRequestStatusId.REQUESTED).ToListAsync();

        result.Should().ContainSingle().Which.Should().Be(new ConnectorProvisioningRequestResponse(
            RequestId, CompanyId, ApplicationId, Bpn, "DEV_OPERATOR_APPROVED", "SUBMITTED", "PENDING", OperatorId.ToString()));
    }

    #endregion

    #region UpdateStatus

    [Fact]
    public async Task UpdateStatus_Ready_RegistersTheConnectorOnceAndStoresTheUrl()
    {
        Current(ConnectorProvisioningRequestStatusId.PROVISIONING);
        var request = CaptureModification();
        var connector = new Connector(Guid.NewGuid(), null!, null!, null!);
        A.CallTo(() => _connectorsRepository.CreateConnector($"EDC {Bpn}", "DE", ConnectorUrl, A<Action<Connector>?>._))
            .Invokes((string _, string _, string _, Action<Connector>? setup) => setup?.Invoke(connector))
            .Returns(connector);

        await _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.READY, "ok", ConnectorUrl));

        connector.TypeId.Should().Be(ConnectorTypeId.CONNECTOR_AS_A_SERVICE);
        connector.StatusId.Should().Be(ConnectorStatusId.ACTIVE);
        connector.ProviderId.Should().Be(CompanyId);
        connector.HostId.Should().Be(HostCompanyId);
        request.StatusId.Should().Be(ConnectorProvisioningRequestStatusId.READY);
        request.ConnectorUrl.Should().Be(ConnectorUrl);
        request.ConnectorId.Should().Be(connector.Id);
        request.DateLastChanged.Should().Be(Now);
        A.CallTo(() => _portalRepositories.SaveAsync()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UpdateStatus_RepeatedReady_IsIdempotentAndCreatesNoSecondConnector()
    {
        Current(ConnectorProvisioningRequestStatusId.READY, Guid.NewGuid());

        await _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.READY, "again", ConnectorUrl));

        A.CallTo(() => _connectorsRepository.CreateConnector(A<string>._, A<string>._, A<string>._, A<Action<Connector>?>._)).MustNotHaveHappened();
        A.CallTo(() => _requestRepository.AttachAndModifyRequest(A<Guid>._, A<Guid>._, A<Action<ConnectorProvisioningRequest>>._)).MustNotHaveHappened();
        A.CallTo(() => _portalRepositories.SaveAsync()).MustNotHaveHappened();
    }

    [Fact]
    public async Task UpdateStatus_ReadyAfterFailedWithExistingConnector_ReusesIt()
    {
        var existing = Guid.NewGuid();
        Current(ConnectorProvisioningRequestStatusId.FAILED, existing);
        var request = CaptureModification();

        await _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.READY, null, ConnectorUrl));

        A.CallTo(() => _connectorsRepository.CreateConnector(A<string>._, A<string>._, A<string>._, A<Action<Connector>?>._)).MustNotHaveHappened();
        request.ConnectorId.Should().Be(existing);
    }

    [Fact]
    public async Task UpdateStatus_Deleted_DeactivatesTheRegisteredConnector()
    {
        var connectorId = Guid.NewGuid();
        Current(ConnectorProvisioningRequestStatusId.READY, connectorId);
        CaptureModification();
        var connector = new Connector(connectorId, null!, null!, null!) { StatusId = ConnectorStatusId.ACTIVE };
        A.CallTo(() => _connectorsRepository.AttachAndModifyConnector(connectorId, null, A<Action<Connector>>._))
            .Invokes((Guid _, Action<Connector>? _, Action<Connector> modify) => modify(connector));

        await _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.DELETED, "deprovisioned", null));

        connector.StatusId.Should().Be(ConnectorStatusId.INACTIVE);
    }

    [Fact]
    public async Task UpdateStatus_DeletedBeforeReady_TouchesNoConnector()
    {
        Current(ConnectorProvisioningRequestStatusId.PROVISIONING);
        var request = CaptureModification();

        await _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.DELETED, "deprovisioned", null));

        A.CallTo(() => _connectorsRepository.AttachAndModifyConnector(A<Guid>._, A<Action<Connector>?>._, A<Action<Connector>>._)).MustNotHaveHappened();
        A.CallTo(() => _connectorsRepository.CreateConnector(A<string>._, A<string>._, A<string>._, A<Action<Connector>?>._)).MustNotHaveHappened();
        request.StatusId.Should().Be(ConnectorProvisioningRequestStatusId.DELETED);
        request.ConnectorId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateStatus_ReadyWhenSaveFails_PropagatesTheErrorSoTheControllerRetries()
    {
        // G9 (2026-10-27): the audited connectors insert failed inside SaveAsync. Connector row and request status are
        // one SaveChanges, so nothing is persisted and the controller's retry reports READY again.
        Current(ConnectorProvisioningRequestStatusId.PROVISIONING);
        CaptureModification();
        A.CallTo(() => _connectorsRepository.CreateConnector(A<string>._, A<string>._, A<string>._, A<Action<Connector>?>._))
            .Returns(new Connector(Guid.NewGuid(), null!, null!, null!));
        A.CallTo(() => _portalRepositories.SaveAsync()).Throws(new UnexpectedConditionException("userId should never be null here"));

        async Task Act() => await _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.READY, "ok", ConnectorUrl));

        await Assert.ThrowsAsync<UnexpectedConditionException>(Act);
        A.CallTo(() => _portalRepositories.SaveAsync()).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(ConnectorProvisioningRequestStatusId.REJECTED, ConnectorProvisioningRequestStatusId.READY)]
    [InlineData(ConnectorProvisioningRequestStatusId.DELETED, ConnectorProvisioningRequestStatusId.PROVISIONING)]
    [InlineData(ConnectorProvisioningRequestStatusId.READY, ConnectorProvisioningRequestStatusId.ACKNOWLEDGED)]
    [InlineData(ConnectorProvisioningRequestStatusId.PROVISIONING, ConnectorProvisioningRequestStatusId.REJECTED)]
    public async Task UpdateStatus_ForbiddenTransition_ThrowsConflictAndWritesNothing(ConnectorProvisioningRequestStatusId from, ConnectorProvisioningRequestStatusId to)
    {
        Current(from);

        async Task Act() => await _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(to, null, ConnectorUrl));

        var ex = await Assert.ThrowsAsync<ConflictException>(Act);
        ex.Message.Should().Be(AdministrationConnectorProvisioningErrors.TRANSITION_NOT_ALLOWED.ToString());
        A.CallTo(() => _portalRepositories.SaveAsync()).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ftp://edc.example/api/v1/dsp")]
    [InlineData("relative/path")]
    [InlineData("javascript:alert(1)")]
    public async Task UpdateStatus_ReadyWithoutValidUrl_IsRejected(string? url)
    {
        Current(ConnectorProvisioningRequestStatusId.PROVISIONING);

        async Task Act() => await _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.READY, null, url));

        await Assert.ThrowsAsync<ControllerArgumentException>(Act);
        A.CallTo(() => _connectorsRepository.CreateConnector(A<string>._, A<string>._, A<string>._, A<Action<Connector>?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task UpdateStatus_RequestedIsNotReportable_AndLongDetailIsRejected()
    {
        var ex = await Assert.ThrowsAsync<ControllerArgumentException>(() =>
            _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.REQUESTED, null, null)));
        ex.Message.Should().Be(AdministrationConnectorProvisioningErrors.STATUS_NOT_REPORTABLE.ToString());

        var ex2 = await Assert.ThrowsAsync<ControllerArgumentException>(() =>
            _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.FAILED, new string('x', 501), null)));
        ex2.Message.Should().Be(AdministrationConnectorProvisioningErrors.DETAIL_TOO_LONG.ToString());
    }

    [Fact]
    public async Task UpdateStatus_UnknownRequest_ThrowsNotFound()
    {
        A.CallTo(() => _requestRepository.GetRequestStatusData(RequestId)).Returns<ConnectorProvisioningRequestStatusData?>(null);

        async Task Act() => await _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.ACKNOWLEDGED, null, null));

        var ex = await Assert.ThrowsAsync<NotFoundException>(Act);
        ex.Message.Should().Be(AdministrationConnectorProvisioningErrors.REQUEST_NOT_FOUND.ToString());
    }

    [Fact]
    public async Task UpdateStatus_UsesTheReadVersionForOptimisticConcurrency()
    {
        Current(ConnectorProvisioningRequestStatusId.REQUESTED);
        CaptureModification();

        await _sut.UpdateStatus(RequestId, new ConnectorProvisioningStatusUpdate(ConnectorProvisioningRequestStatusId.ACKNOWLEDGED, null, null));

        A.CallTo(() => _requestRepository.AttachAndModifyRequest(RequestId, Version, A<Action<ConnectorProvisioningRequest>>._)).MustHaveHappenedOnceExactly();
    }

    #endregion

    #region CreateDevOperatorRequest

    [Fact]
    public async Task CreateDevOperatorRequest_Disabled_IsForbidden()
    {
        async Task Act() => await _sut.CreateDevOperatorRequest(new ConnectorProvisioningDevRequest(ApplicationId));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(Act);
        ex.Message.Should().Be(AdministrationConnectorProvisioningErrors.DEV_REQUESTS_DISABLED.ToString());
        A.CallTo(() => _requestRepository.CreateRequest(A<Guid>._, A<Guid>._, A<string>._, A<ConnectorProvisioningBasisId>._, A<Guid?>._, A<DateTimeOffset>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task CreateDevOperatorRequest_Enabled_RecordsTheAuthenticatedOperator()
    {
        _settings.DevOperatorRequestsEnabled = true;
        A.CallTo(() => _requestRepository.GetApplicationData(ApplicationId))
            .Returns(new ConnectorProvisioningApplicationData(CompanyId, CompanyApplicationStatusId.SUBMITTED, Bpn));
        A.CallTo(() => _requestRepository.HasLiveRequest(CompanyId)).Returns(false);
        var created = new ConnectorProvisioningRequest(Guid.NewGuid(), CompanyId, ApplicationId, Bpn,
            ConnectorProvisioningBasisId.DEV_OPERATOR_APPROVED, ConnectorProvisioningRequestStatusId.REQUESTED, Now);
        A.CallTo(() => _requestRepository.CreateRequest(CompanyId, ApplicationId, Bpn, ConnectorProvisioningBasisId.DEV_OPERATOR_APPROVED, OperatorId, Now))
            .Returns(created);

        var id = await _sut.CreateDevOperatorRequest(new ConnectorProvisioningDevRequest(ApplicationId));

        id.Should().Be(created.Id);
        A.CallTo(() => _portalRepositories.SaveAsync()).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(CompanyApplicationStatusId.CREATED)]
    [InlineData(CompanyApplicationStatusId.DECLINED)]
    [InlineData(CompanyApplicationStatusId.CANCELLED_BY_CUSTOMER)]
    public async Task CreateDevOperatorRequest_WrongApplicationStatus_IsConflict(CompanyApplicationStatusId status)
    {
        _settings.DevOperatorRequestsEnabled = true;
        A.CallTo(() => _requestRepository.GetApplicationData(ApplicationId))
            .Returns(new ConnectorProvisioningApplicationData(CompanyId, status, Bpn));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateDevOperatorRequest(new ConnectorProvisioningDevRequest(ApplicationId)));

        ex.Message.Should().Be(AdministrationConnectorProvisioningErrors.APPLICATION_STATUS_NOT_ALLOWED.ToString());
    }

    [Fact]
    public async Task CreateDevOperatorRequest_MissingBpnOrLiveRequestOrUnknownApplication_IsRefused()
    {
        _settings.DevOperatorRequestsEnabled = true;
        A.CallTo(() => _requestRepository.GetApplicationData(ApplicationId))
            .Returns(new ConnectorProvisioningApplicationData(CompanyId, CompanyApplicationStatusId.SUBMITTED, null));
        (await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateDevOperatorRequest(new ConnectorProvisioningDevRequest(ApplicationId))))
            .Message.Should().Be(AdministrationConnectorProvisioningErrors.BPN_MISSING.ToString());

        A.CallTo(() => _requestRepository.GetApplicationData(ApplicationId))
            .Returns(new ConnectorProvisioningApplicationData(CompanyId, CompanyApplicationStatusId.SUBMITTED, Bpn));
        A.CallTo(() => _requestRepository.HasLiveRequest(CompanyId)).Returns(true);
        (await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateDevOperatorRequest(new ConnectorProvisioningDevRequest(ApplicationId))))
            .Message.Should().Be(AdministrationConnectorProvisioningErrors.LIVE_REQUEST_EXISTS.ToString());

        A.CallTo(() => _requestRepository.GetApplicationData(ApplicationId)).Returns<ConnectorProvisioningApplicationData?>(null);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateDevOperatorRequest(new ConnectorProvisioningDevRequest(ApplicationId)));

        A.CallTo(() => _portalRepositories.SaveAsync()).MustNotHaveHappened();
    }

    #endregion

    #region Authorization

    [Theory]
    [InlineData(nameof(ConnectorProvisioningController.GetRequests), "connector_provisioner", null)]
    [InlineData(nameof(ConnectorProvisioningController.UpdateStatus), "connector_provisioner", PolicyTypes.ValidIdentity)]
    [InlineData(nameof(ConnectorProvisioningController.CreateDevOperatorRequest), "approve_new_partner", PolicyTypes.ValidIdentity)]
    public void Endpoints_RequireExactlyTheirRoleAndPolicy(string method, string role, string? policy)
    {
        var attributes = typeof(ConnectorProvisioningController).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>().ToList();

        var expectedPolicies = policy == null ? Array.Empty<string>() : new[] { policy };
        attributes.Where(a => a.Roles != null).Select(a => a.Roles!).Should().Equal(new[] { role });
        attributes.Where(a => a.Policy != null).Select(a => a.Policy!).Should().Equal(expectedPolicies);
        typeof(ConnectorProvisioningController).GetCustomAttributes<AllowAnonymousAttribute>().Should().BeEmpty();
    }

    // The controller's access token (Keycloak 25, client sa-connector-provisioner): the service-account username is
    // not a Guid, client_id comes from the client's "Client ID" session-note mapper, sub is the service-account user
    // id (verified read-only in the CX-Central realm database on 2026-10-27).
    private static ClaimsPrincipal ControllerPrincipal() => new(new ClaimsIdentity(new[]
    {
        new Claim(PortalClaimTypes.PreferredUserName, "service-account-sa-connector-provisioner"),
        new Claim(PortalClaimTypes.ClientId, "sa-connector-provisioner"),
        new Claim(PortalClaimTypes.Sub, "7c7e0b6c-9b39-4cd4-894c-18bc0abac668")
    }));

    private static (AuthorizationHandlerContext Context, ClaimsIdentityDataBuilder Identity, MandatoryIdentityClaimHandler Handler) ValidIdentityCheck(ITechnicalUserRepository technicalUsers)
    {
        var repositories = A.Fake<IPortalRepositories>();
        var identities = A.Fake<IIdentityRepository>();
        A.CallTo(() => identities.GetActiveIdentityDataByUserEntityId(A<string>._)).Returns<(Guid, IdentityTypeId, Guid)>(default);
        A.CallTo(() => repositories.GetInstance<IIdentityRepository>()).Returns(identities);
        A.CallTo(() => repositories.GetInstance<ITechnicalUserRepository>()).Returns(technicalUsers);
        var identity = new ClaimsIdentityDataBuilder();
        var context = new AuthorizationHandlerContext(new[] { new MandatoryIdentityClaimRequirement(PolicyTypeId.ValidIdentity) }, ControllerPrincipal(), null);
        return (context, identity, new MandatoryIdentityClaimHandler(identity, repositories, A.Fake<ILogger<MandatoryIdentityClaimHandler>>()));
    }

    [Fact]
    public async Task ControllerToken_WithRegisteredTechnicalUser_ResolvesTheIdentityTheAuditNeeds()
    {
        var technicalUserId = Guid.NewGuid();
        var technicalUsers = A.Fake<ITechnicalUserRepository>();
        A.CallTo(() => technicalUsers.GetTechnicalUserDataByClientId("sa-connector-provisioner")).Returns((technicalUserId, HostCompanyId));
        var (context, identity, handler) = ValidIdentityCheck(technicalUsers);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        identity.IdentityId.Should().Be(technicalUserId);
        identity.IdentityTypeId.Should().Be(IdentityTypeId.TECHNICAL_USER);
        identity.CompanyId.Should().Be(HostCompanyId);
    }

    [Fact]
    public async Task ControllerToken_WithoutTechnicalUser_IsForbiddenInsteadOfFailingInTheAudit()
    {
        var technicalUsers = A.Fake<ITechnicalUserRepository>();
        A.CallTo(() => technicalUsers.GetTechnicalUserDataByClientId(A<string>._)).Returns<(Guid, Guid)>(default);
        var (context, identity, handler) = ValidIdentityCheck(technicalUsers);

        await handler.HandleAsync(context);

        // 403 before the action runs; without the policy the action ran and the audit threw (HTTP 500).
        context.HasSucceeded.Should().BeFalse();
        identity.Status.Should().Be(IClaimsIdentityDataBuilderStatus.Empty);
        Assert.Throws<UnexpectedConditionException>(() => identity.IdentityId);
    }

    [Fact]
    public void Transitions_TerminalStatesAreFinal()
    {
        foreach (var to in Enum.GetValues<ConnectorProvisioningRequestStatusId>())
        {
            ConnectorProvisioningBusinessLogic.IsAllowedTransition(ConnectorProvisioningRequestStatusId.REJECTED, to).Should().BeFalse();
            ConnectorProvisioningBusinessLogic.IsAllowedTransition(ConnectorProvisioningRequestStatusId.DELETED, to).Should().BeFalse();
            ConnectorProvisioningBusinessLogic.IsAllowedTransition(to, ConnectorProvisioningRequestStatusId.REQUESTED).Should().BeFalse();
        }
    }

    #endregion
}
