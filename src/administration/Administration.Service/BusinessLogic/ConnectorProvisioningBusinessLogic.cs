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

using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.Administration.Service.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Administration.Service.Models;
using Org.Eclipse.TractusX.Portal.Backend.Framework.DateTimeProvider;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Identity;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using System.Collections.Immutable;

namespace Org.Eclipse.TractusX.Portal.Backend.Administration.Service.BusinessLogic;

/// <summary>
/// BE-293 automatic connector provisioning: the controller pulls requests and reports status; READY registers
/// the provisioned connector (CONNECTOR_AS_A_SERVICE, hosted by the operator company); DEV operators may
/// approve an application that is still in review when explicitly enabled.
/// </summary>
public class ConnectorProvisioningBusinessLogic(
    IPortalRepositories portalRepositories,
    IIdentityService identityService,
    IDateTimeProvider dateTimeProvider,
    IOptions<ConnectorProvisioningSettings> options)
    : IConnectorProvisioningBusinessLogic
{
    public const int MaxDetailLength = 500;

    private static readonly ImmutableDictionary<ConnectorProvisioningRequestStatusId, ImmutableHashSet<ConnectorProvisioningRequestStatusId>> AllowedTransitions =
        new Dictionary<ConnectorProvisioningRequestStatusId, ImmutableHashSet<ConnectorProvisioningRequestStatusId>>
        {
            [ConnectorProvisioningRequestStatusId.REQUESTED] = [ConnectorProvisioningRequestStatusId.ACKNOWLEDGED, ConnectorProvisioningRequestStatusId.PROVISIONING, ConnectorProvisioningRequestStatusId.READY, ConnectorProvisioningRequestStatusId.FAILED, ConnectorProvisioningRequestStatusId.REJECTED],
            [ConnectorProvisioningRequestStatusId.ACKNOWLEDGED] = [ConnectorProvisioningRequestStatusId.PROVISIONING, ConnectorProvisioningRequestStatusId.READY, ConnectorProvisioningRequestStatusId.FAILED, ConnectorProvisioningRequestStatusId.REJECTED, ConnectorProvisioningRequestStatusId.DELETED],
            [ConnectorProvisioningRequestStatusId.PROVISIONING] = [ConnectorProvisioningRequestStatusId.READY, ConnectorProvisioningRequestStatusId.FAILED, ConnectorProvisioningRequestStatusId.DELETED],
            [ConnectorProvisioningRequestStatusId.READY] = [ConnectorProvisioningRequestStatusId.FAILED, ConnectorProvisioningRequestStatusId.DELETED],
            [ConnectorProvisioningRequestStatusId.FAILED] = [ConnectorProvisioningRequestStatusId.PROVISIONING, ConnectorProvisioningRequestStatusId.READY, ConnectorProvisioningRequestStatusId.DELETED],
            [ConnectorProvisioningRequestStatusId.REJECTED] = [],
            [ConnectorProvisioningRequestStatusId.DELETED] = []
        }.ToImmutableDictionary();

    private readonly ConnectorProvisioningSettings _settings = options.Value;

    public static bool IsAllowedTransition(ConnectorProvisioningRequestStatusId from, ConnectorProvisioningRequestStatusId to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <inheritdoc />
    public async IAsyncEnumerable<ConnectorProvisioningRequestResponse> GetRequests(ConnectorProvisioningRequestStatusId status)
    {
        await foreach (var x in portalRepositories.GetInstance<IConnectorProvisioningRequestRepository>()
                           .GetRequestsByStatus(status).ConfigureAwait(false))
        {
            yield return new ConnectorProvisioningRequestResponse(
                x.RequestId,
                x.CompanyId,
                x.ApplicationId,
                x.BusinessPartnerNumber,
                x.BasisId.ToString(),
                x.ApplicationStatusId.ToString(),
                x.CompanyStatusId.ToString(),
                x.ApprovedBy?.ToString());
        }
    }

    /// <inheritdoc />
    public async Task UpdateStatus(Guid requestId, ConnectorProvisioningStatusUpdate update)
    {
        if (update.Status == ConnectorProvisioningRequestStatusId.REQUESTED || !Enum.IsDefined(update.Status))
        {
            throw ControllerArgumentException.Create(AdministrationConnectorProvisioningErrors.STATUS_NOT_REPORTABLE, [new("status", update.Status.ToString())]);
        }
        if (update.Detail?.Length > MaxDetailLength)
        {
            throw ControllerArgumentException.Create(AdministrationConnectorProvisioningErrors.DETAIL_TOO_LONG, [new("maxLength", MaxDetailLength.ToString())]);
        }
        if (update.Status == ConnectorProvisioningRequestStatusId.READY)
        {
            if (string.IsNullOrWhiteSpace(update.ConnectorUrl))
            {
                throw ControllerArgumentException.Create(AdministrationConnectorProvisioningErrors.CONNECTOR_URL_REQUIRED);
            }
            if (!Uri.TryCreate(update.ConnectorUrl, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
                || update.ConnectorUrl.Length > 255)
            {
                throw ControllerArgumentException.Create(AdministrationConnectorProvisioningErrors.CONNECTOR_URL_INVALID);
            }
        }

        var requestRepository = portalRepositories.GetInstance<IConnectorProvisioningRequestRepository>();
        var current = await requestRepository.GetRequestStatusData(requestId).ConfigureAwait(ConfigureAwaitOptions.None)
            ?? throw NotFoundException.Create(AdministrationConnectorProvisioningErrors.REQUEST_NOT_FOUND, [new(nameof(requestId), requestId.ToString())]);

        if (current.StatusId == update.Status)
        {
            return;     // idempotent: the controller re-reports a status after a retry
        }
        if (!IsAllowedTransition(current.StatusId, update.Status))
        {
            throw ConflictException.Create(AdministrationConnectorProvisioningErrors.TRANSITION_NOT_ALLOWED,
                [new("from", current.StatusId.ToString()), new("to", update.Status.ToString())]);
        }

        var now = dateTimeProvider.OffsetNow;
        var connectorsRepository = portalRepositories.GetInstance<IConnectorsRepository>();
        var connectorId = current.ConnectorId;
        if (update.Status == ConnectorProvisioningRequestStatusId.READY && connectorId == null)
        {
            connectorId = connectorsRepository.CreateConnector($"EDC {current.BusinessPartnerNumber}", _settings.ConnectorLocationId, update.ConnectorUrl!, connector =>
            {
                connector.TypeId = ConnectorTypeId.CONNECTOR_AS_A_SERVICE;
                connector.StatusId = ConnectorStatusId.ACTIVE;
                connector.ProviderId = current.CompanyId;
                connector.HostId = _settings.HostCompanyId;
                connector.DateLastChanged = now;
                connector.SdSkippedDate = now;          // self-description is out of scope for BE-293 DEV
            }).Id;
        }
        else if (update.Status == ConnectorProvisioningRequestStatusId.DELETED && connectorId != null)
        {
            connectorsRepository.AttachAndModifyConnector(connectorId.Value, null, connector =>
            {
                connector.StatusId = ConnectorStatusId.INACTIVE;
                connector.DateLastChanged = now;
            });
        }

        requestRepository.AttachAndModifyRequest(requestId, current.Version, request =>
        {
            request.StatusId = update.Status;
            request.StatusDetail = update.Detail;
            request.DateLastChanged = now;
            if (update.Status == ConnectorProvisioningRequestStatusId.READY)
            {
                request.ConnectorUrl = update.ConnectorUrl;
            }
            request.ConnectorId = connectorId;
        });
        await portalRepositories.SaveAsync().ConfigureAwait(ConfigureAwaitOptions.None);
    }

    /// <inheritdoc />
    public async Task<Guid> CreateDevOperatorRequest(ConnectorProvisioningDevRequest request)
    {
        if (!_settings.DevOperatorRequestsEnabled)
        {
            throw ForbiddenException.Create(AdministrationConnectorProvisioningErrors.DEV_REQUESTS_DISABLED);
        }
        var requestRepository = portalRepositories.GetInstance<IConnectorProvisioningRequestRepository>();
        var application = await requestRepository.GetApplicationData(request.ApplicationId).ConfigureAwait(ConfigureAwaitOptions.None)
            ?? throw NotFoundException.Create(AdministrationConnectorProvisioningErrors.APPLICATION_NOT_FOUND, [new("applicationId", request.ApplicationId.ToString())]);
        if (application.ApplicationStatusId != CompanyApplicationStatusId.SUBMITTED && application.ApplicationStatusId != CompanyApplicationStatusId.CONFIRMED)
        {
            throw ConflictException.Create(AdministrationConnectorProvisioningErrors.APPLICATION_STATUS_NOT_ALLOWED,
                [new("applicationId", request.ApplicationId.ToString()), new("status", application.ApplicationStatusId.ToString())]);
        }
        if (string.IsNullOrWhiteSpace(application.BusinessPartnerNumber))
        {
            throw ConflictException.Create(AdministrationConnectorProvisioningErrors.BPN_MISSING, [new("applicationId", request.ApplicationId.ToString())]);
        }
        if (await requestRepository.HasLiveRequest(application.CompanyId).ConfigureAwait(ConfigureAwaitOptions.None))
        {
            throw ConflictException.Create(AdministrationConnectorProvisioningErrors.LIVE_REQUEST_EXISTS, [new("companyId", application.CompanyId.ToString())]);
        }
        // The approving operator is the authenticated caller, never a value from the request body.
        var created = requestRepository.CreateRequest(application.CompanyId, request.ApplicationId, application.BusinessPartnerNumber,
            ConnectorProvisioningBasisId.DEV_OPERATOR_APPROVED, identityService.IdentityData.IdentityId, dateTimeProvider.OffsetNow);
        await portalRepositories.SaveAsync().ConfigureAwait(ConfigureAwaitOptions.None);
        return created.Id;
    }
}
