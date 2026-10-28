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
using Microsoft.AspNetCore.Mvc;
using Org.Eclipse.TractusX.Portal.Backend.Administration.Service.BusinessLogic;
using Org.Eclipse.TractusX.Portal.Backend.Administration.Service.Models;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling.Web;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Web;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Web.Identity;

namespace Org.Eclipse.TractusX.Portal.Backend.Administration.Service.Controllers;

/// <summary>
/// BE-293 automatic connector provisioning. The Connector Provisioning Controller (technical user with the
/// single role connector_provisioner) pulls requests and reports status; operators may create DEV requests.
/// </summary>
[EnvironmentRoute("MVC_ROUTING_BASEPATH", "connector-provisioning")]
[ApiController]
[Produces("application/json")]
public class ConnectorProvisioningController(IConnectorProvisioningBusinessLogic logic) : ControllerBase
{
    /// <summary>
    /// Provisioning requests in the given status (the controller pulls REQUESTED).
    /// </summary>
    /// <remarks>Example: GET /api/administration/connector-provisioning/requests?status=REQUESTED</remarks>
    [HttpGet]
    [Route("requests")]
    [Authorize(Roles = "connector_provisioner")]
    [ProducesResponseType(typeof(IAsyncEnumerable<ConnectorProvisioningRequestResponse>), StatusCodes.Status200OK)]
    public IAsyncEnumerable<ConnectorProvisioningRequestResponse> GetRequests([FromQuery] ConnectorProvisioningRequestStatusId status = ConnectorProvisioningRequestStatusId.REQUESTED) =>
        logic.GetRequests(status);

    /// <summary>
    /// Lifecycle status reported by the controller; READY registers the connector.
    /// </summary>
    /// <remarks>
    /// Example: PUT /api/administration/connector-provisioning/requests/{requestId}/status<br />
    /// READY and DELETED write the audited connectors row (last editor), so the caller must resolve to a Portal
    /// identity: the controller's client is registered as a technical user (client_id claim).
    /// </remarks>
    [HttpPut]
    [Route("requests/{requestId:guid}/status")]
    [Authorize(Roles = "connector_provisioner")]
    [Authorize(Policy = PolicyTypes.ValidIdentity)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<NoContentResult> UpdateStatus([FromRoute] Guid requestId, [FromBody] ConnectorProvisioningStatusUpdate update)
    {
        await logic.UpdateStatus(requestId, update).ConfigureAwait(ConfigureAwaitOptions.None);
        return NoContent();
    }

    /// <summary>
    /// DEV only (disabled unless ConnectorProvisioning:DevOperatorRequestsEnabled): an operator approves
    /// provisioning for an application that is still in review. The approver is the authenticated operator.
    /// </summary>
    /// <remarks>Example: POST /api/administration/connector-provisioning/requests/dev</remarks>
    [HttpPost]
    [Route("requests/dev")]
    [Authorize(Roles = "approve_new_partner")]
    [Authorize(Policy = PolicyTypes.ValidIdentity)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<CreatedResult> CreateDevOperatorRequest([FromBody] ConnectorProvisioningDevRequest request)
    {
        var id = await logic.CreateDevOperatorRequest(request).ConfigureAwait(ConfigureAwaitOptions.None);
        return Created($"requests/{id}", id);
    }
}
