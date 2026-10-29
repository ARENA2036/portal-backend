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

using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling.Service;
using System.Collections.Immutable;

namespace Org.Eclipse.TractusX.Portal.Backend.Administration.Service.ErrorHandling;

public class AdministrationConnectorProvisioningErrorMessageContainer : IErrorMessageContainer
{
    private static readonly IReadOnlyDictionary<int, string> _messageContainer = ImmutableDictionary.CreateRange<int, string>([
        new((int)AdministrationConnectorProvisioningErrors.REQUEST_NOT_FOUND, "connector provisioning request {requestId} does not exist"),
        new((int)AdministrationConnectorProvisioningErrors.TRANSITION_NOT_ALLOWED, "status transition {from} -> {to} is not allowed"),
        new((int)AdministrationConnectorProvisioningErrors.CONNECTOR_URL_REQUIRED, "status READY requires a connectorUrl"),
        new((int)AdministrationConnectorProvisioningErrors.CONNECTOR_URL_INVALID, "connectorUrl must be an absolute http(s) URL"),
        new((int)AdministrationConnectorProvisioningErrors.DETAIL_TOO_LONG, "detail must not exceed {maxLength} characters"),
        new((int)AdministrationConnectorProvisioningErrors.DEV_REQUESTS_DISABLED, "DEV operator provisioning requests are disabled in this environment"),
        new((int)AdministrationConnectorProvisioningErrors.APPLICATION_NOT_FOUND, "application {applicationId} does not exist"),
        new((int)AdministrationConnectorProvisioningErrors.APPLICATION_STATUS_NOT_ALLOWED, "application {applicationId} is in status {status}; only SUBMITTED or CONFIRMED applications can be provisioned"),
        new((int)AdministrationConnectorProvisioningErrors.BPN_MISSING, "company of application {applicationId} has no business partner number"),
        new((int)AdministrationConnectorProvisioningErrors.LIVE_REQUEST_EXISTS, "company {companyId} already has a live connector provisioning request"),
        new((int)AdministrationConnectorProvisioningErrors.STATUS_NOT_REPORTABLE, "status {status} cannot be reported by the controller")
    ]);

    public Type Type { get => typeof(AdministrationConnectorProvisioningErrors); }
    public IReadOnlyDictionary<int, string> MessageContainer { get => _messageContainer; }
}

public enum AdministrationConnectorProvisioningErrors
{
    REQUEST_NOT_FOUND,
    TRANSITION_NOT_ALLOWED,
    CONNECTOR_URL_REQUIRED,
    CONNECTOR_URL_INVALID,
    DETAIL_TOO_LONG,
    DEV_REQUESTS_DISABLED,
    APPLICATION_NOT_FOUND,
    APPLICATION_STATUS_NOT_ALLOWED,
    BPN_MISSING,
    LIVE_REQUEST_EXISTS,
    STATUS_NOT_REPORTABLE
}
