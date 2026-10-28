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

using Microsoft.EntityFrameworkCore;
using Org.Eclipse.TractusX.Portal.Backend.Framework.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Models;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Entities;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;

namespace Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;

public class ConnectorProvisioningRequestRepository(PortalDbContext dbContext) : IConnectorProvisioningRequestRepository
{
    /// <inheritdoc />
    public ConnectorProvisioningRequest CreateRequest(Guid companyId, Guid applicationId, string businessPartnerNumber,
        ConnectorProvisioningBasisId basisId, Guid? approvedBy, DateTimeOffset now)
    {
        var request = new ConnectorProvisioningRequest(Guid.NewGuid(), companyId, applicationId, businessPartnerNumber,
            basisId, ConnectorProvisioningRequestStatusId.REQUESTED, now)
        {
            ApprovedBy = approvedBy
        };
        return dbContext.ConnectorProvisioningRequests.Add(request).Entity;
    }

    /// <inheritdoc />
    public Task<bool> HasLiveRequest(Guid companyId) =>
        dbContext.ConnectorProvisioningRequests
            .AnyAsync(x => x.CompanyId == companyId &&
                           x.StatusId != ConnectorProvisioningRequestStatusId.REJECTED &&
                           x.StatusId != ConnectorProvisioningRequestStatusId.DELETED);

    /// <inheritdoc />
    public IAsyncEnumerable<ConnectorProvisioningRequestData> GetRequestsByStatus(ConnectorProvisioningRequestStatusId statusId) =>
        dbContext.ConnectorProvisioningRequests
            .AsNoTracking()
            .Where(x => x.StatusId == statusId)
            .OrderBy(x => x.DateCreated)
            .Select(x => new ConnectorProvisioningRequestData(
                x.Id,
                x.CompanyId,
                x.ApplicationId,
                x.BusinessPartnerNumber,
                x.BasisId,
                x.Application!.ApplicationStatusId,
                x.Company!.CompanyStatusId,
                x.ApprovedBy))
            .AsAsyncEnumerable();

    /// <inheritdoc />
    public Task<ConnectorProvisioningRequestStatusData?> GetRequestStatusData(Guid requestId) =>
        dbContext.ConnectorProvisioningRequests
            .AsNoTracking()
            .Where(x => x.Id == requestId)
            .Select(x => new ConnectorProvisioningRequestStatusData(x.StatusId, x.CompanyId, x.BusinessPartnerNumber, x.ConnectorId, x.Version))
            .SingleOrDefaultAsync();

    /// <inheritdoc />
    public Task<ConnectorProvisioningApplicationData?> GetApplicationData(Guid applicationId) =>
        dbContext.CompanyApplications
            .AsNoTracking()
            .Where(x => x.Id == applicationId)
            .Select(x => new ConnectorProvisioningApplicationData(x.CompanyId, x.ApplicationStatusId, x.Company!.BusinessPartnerNumber))
            .SingleOrDefaultAsync();

    /// <inheritdoc />
    public void AttachAndModifyRequest(Guid requestId, Guid version, Action<ConnectorProvisioningRequest> modify)
    {
        var request = new ConnectorProvisioningRequest(requestId) { Version = version };
        dbContext.Attach(request);
        modify(request);
        request.UpdateVersion();
    }
}
