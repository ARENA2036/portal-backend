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
using System.Text.Json.Serialization;

namespace Org.Eclipse.TractusX.Portal.Backend.Administration.Service.Models;

/// <summary>Request row as pulled by the Connector Provisioning Controller (contract: connector-provisioner/adapters/portal_http.py).</summary>
public record ConnectorProvisioningRequestResponse(
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("companyId")] Guid CompanyId,
    [property: JsonPropertyName("applicationId")] Guid ApplicationId,
    [property: JsonPropertyName("bpn")] string Bpn,
    [property: JsonPropertyName("basis")] string Basis,
    [property: JsonPropertyName("applicationStatus")] string ApplicationStatus,
    [property: JsonPropertyName("companyStatus")] string CompanyStatus,
    [property: JsonPropertyName("approvedBy")] string? ApprovedBy);

/// <summary>Status report from the controller.</summary>
public record ConnectorProvisioningStatusUpdate(
    [property: JsonPropertyName("status")] ConnectorProvisioningRequestStatusId Status,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("connectorUrl")] string? ConnectorUrl);

/// <summary>DEV-only operator approval of an application that is still in review.</summary>
public record ConnectorProvisioningDevRequest(
    [property: JsonPropertyName("applicationId")] Guid ApplicationId);
