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

using Org.Eclipse.TractusX.Portal.Backend.Framework.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using System.ComponentModel.DataAnnotations;

namespace Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Entities;

/// <summary>
/// Transactional outbox row for automatic connector provisioning (BE-293). Written in the same unit of
/// work that confirms the application, pulled by the Connector Provisioning Controller.
/// </summary>
public class ConnectorProvisioningRequest : IBaseEntity, IVersionedEntity
{
    private ConnectorProvisioningRequest()
    {
        BusinessPartnerNumber = null!;
    }

    public ConnectorProvisioningRequest(Guid id, Guid companyId, Guid applicationId, string businessPartnerNumber,
        ConnectorProvisioningBasisId basisId, ConnectorProvisioningRequestStatusId statusId, DateTimeOffset dateCreated)
        : this()
    {
        Id = id;
        CompanyId = companyId;
        ApplicationId = applicationId;
        BusinessPartnerNumber = businessPartnerNumber;
        BasisId = basisId;
        StatusId = statusId;
        DateCreated = dateCreated;
        DateLastChanged = dateCreated;
        Version = Guid.NewGuid();
    }

    /// <summary>Constructor for attaching an existing row (AttachAndModify pattern).</summary>
    public ConnectorProvisioningRequest(Guid id) : this()
    {
        Id = id;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }

    [MaxLength(20)]
    public string BusinessPartnerNumber { get; private set; }

    public ConnectorProvisioningBasisId BasisId { get; private set; }
    public ConnectorProvisioningRequestStatusId StatusId { get; set; }

    [MaxLength(500)]
    public string? StatusDetail { get; set; }

    [MaxLength(255)]
    public string? ConnectorUrl { get; set; }

    /// <summary>Identity id of the operator who approved a DEV_OPERATOR_APPROVED request; null otherwise.</summary>
    public Guid? ApprovedBy { get; set; }

    /// <summary>Connector registry entry created when the controller reports READY.</summary>
    public Guid? ConnectorId { get; set; }

    public DateTimeOffset DateCreated { get; private set; }
    public DateTimeOffset DateLastChanged { get; set; }

    [ConcurrencyCheck]
    public Guid Version { get; set; }

    // Navigation properties
    public virtual Company? Company { get; private set; }
    public virtual CompanyApplication? Application { get; private set; }
    public virtual Connector? Connector { get; private set; }
    public virtual ConnectorProvisioningRequestStatus? Status { get; private set; }
    public virtual ConnectorProvisioningBasis? Basis { get; private set; }
}
