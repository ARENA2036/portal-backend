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

using Org.Eclipse.TractusX.Portal.Backend.Framework.Models.Validation;
using System.ComponentModel.DataAnnotations;

namespace Org.Eclipse.TractusX.Portal.Backend.Administration.Service.BusinessLogic;

/// <summary>
/// Settings for the BE-293 automatic connector provisioning API.
/// </summary>
public class ConnectorProvisioningSettings
{
    /// <summary>
    /// Enables POST connector-provisioning/requests/dev (operator approval of applications still in review).
    /// Must stay false outside DEV.
    /// </summary>
    public bool DevOperatorRequestsEnabled { get; set; }

    /// <summary>
    /// Company that hosts the provisioned connectors (the operator); becomes the connector's host company.
    /// Null records no host. Optional so that deployments without the controller start unchanged.
    /// </summary>
    public Guid? HostCompanyId { get; set; }

    /// <summary>
    /// Country (alpha-2) recorded as the location of provisioned connectors.
    /// </summary>
    [StringLength(2, MinimumLength = 2)]
    public string ConnectorLocationId { get; set; } = "DE";
}

public static class ConnectorProvisioningSettingsExtensions
{
    public static IServiceCollection ConfigureConnectorProvisioningSettings(
        this IServiceCollection services,
        IConfigurationSection section)
    {
        services.AddOptions<ConnectorProvisioningSettings>()
            .Bind(section)
            .EnvironmentalValidation(section);
        return services;
    }
}
