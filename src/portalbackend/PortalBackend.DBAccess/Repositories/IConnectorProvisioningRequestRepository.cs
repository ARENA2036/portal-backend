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

using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Models;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Entities;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;

namespace Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;

/// <summary>BE-293 automatic connector provisioning outbox.</summary>
public interface IConnectorProvisioningRequestRepository
{
    /// <summary>Adds a REQUESTED row to the current unit of work (saved with the caller's other changes).</summary>
    ConnectorProvisioningRequest CreateRequest(Guid companyId, Guid applicationId, string businessPartnerNumber,
        ConnectorProvisioningBasisId basisId, Guid? approvedBy, DateTimeOffset now);

    /// <summary>True if the company has a request that is neither REJECTED nor DELETED.</summary>
    Task<bool> HasLiveRequest(Guid companyId);

    IAsyncEnumerable<ConnectorProvisioningRequestData> GetRequestsByStatus(ConnectorProvisioningRequestStatusId statusId);

    Task<ConnectorProvisioningRequestStatusData?> GetRequestStatusData(Guid requestId);

    Task<ConnectorProvisioningApplicationData?> GetApplicationData(Guid applicationId);

    /// <summary>Attaches the row with its known version (optimistic concurrency) and applies the modification.</summary>
    void AttachAndModifyRequest(Guid requestId, Guid version, Action<ConnectorProvisioningRequest> modify);
}
