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

using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;

namespace Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Models;

/// <summary>A provisioning request as the Connector Provisioning Controller pulls it (ids and states only).</summary>
public record ConnectorProvisioningRequestData(
    Guid RequestId,
    Guid CompanyId,
    Guid ApplicationId,
    string BusinessPartnerNumber,
    ConnectorProvisioningBasisId BasisId,
    CompanyApplicationStatusId ApplicationStatusId,
    CompanyStatusId CompanyStatusId,
    Guid? ApprovedBy);

/// <summary>Current state of a request, for status transitions.</summary>
public record ConnectorProvisioningRequestStatusData(
    ConnectorProvisioningRequestStatusId StatusId,
    Guid CompanyId,
    string BusinessPartnerNumber,
    Guid? ConnectorId,
    Guid Version);

/// <summary>Application facts needed to accept a DEV operator request.</summary>
public record ConnectorProvisioningApplicationData(
    Guid CompanyId,
    CompanyApplicationStatusId ApplicationStatusId,
    string? BusinessPartnerNumber);
