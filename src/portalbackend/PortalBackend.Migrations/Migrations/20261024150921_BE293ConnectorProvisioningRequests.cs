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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Org.Eclipse.TractusX.Portal.Backend.PortalBackend.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class BE293ConnectorProvisioningRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "connector_provisioning_bases",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_provisioning_bases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "connector_provisioning_request_statuses",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_provisioning_request_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "connector_provisioning_requests",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_partner_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    basis_id = table.Column<int>(type: "integer", nullable: false),
                    status_id = table.Column<int>(type: "integer", nullable: false),
                    status_detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    connector_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    connector_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    date_last_changed = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_provisioning_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_connector_provisioning_requests_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "portal",
                        principalTable: "companies",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_connector_provisioning_requests_company_applications_applic",
                        column: x => x.application_id,
                        principalSchema: "portal",
                        principalTable: "company_applications",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_connector_provisioning_requests_connector_provisioning_base",
                        column: x => x.basis_id,
                        principalSchema: "portal",
                        principalTable: "connector_provisioning_bases",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_connector_provisioning_requests_connector_provisioning_requ",
                        column: x => x.status_id,
                        principalSchema: "portal",
                        principalTable: "connector_provisioning_request_statuses",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_connector_provisioning_requests_connectors_connector_id",
                        column: x => x.connector_id,
                        principalSchema: "portal",
                        principalTable: "connectors",
                        principalColumn: "id");
                });

            migrationBuilder.InsertData(
                schema: "portal",
                table: "connector_provisioning_bases",
                columns: new[] { "id", "label" },
                values: new object[,]
                {
                    { 1, "APPLICATION_CONFIRMED" },
                    { 2, "DEV_OPERATOR_APPROVED" }
                });

            migrationBuilder.InsertData(
                schema: "portal",
                table: "connector_provisioning_request_statuses",
                columns: new[] { "id", "label" },
                values: new object[,]
                {
                    { 1, "REQUESTED" },
                    { 2, "ACKNOWLEDGED" },
                    { 3, "PROVISIONING" },
                    { 4, "READY" },
                    { 5, "FAILED" },
                    { 6, "REJECTED" },
                    { 7, "DELETED" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_connector_provisioning_requests_application_id",
                schema: "portal",
                table: "connector_provisioning_requests",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_connector_provisioning_requests_basis_id",
                schema: "portal",
                table: "connector_provisioning_requests",
                column: "basis_id");

            migrationBuilder.CreateIndex(
                name: "ix_connector_provisioning_requests_company_id",
                schema: "portal",
                table: "connector_provisioning_requests",
                column: "company_id",
                unique: true,
                filter: "status_id not in (6, 7)");

            migrationBuilder.CreateIndex(
                name: "ix_connector_provisioning_requests_connector_id",
                schema: "portal",
                table: "connector_provisioning_requests",
                column: "connector_id");

            migrationBuilder.CreateIndex(
                name: "ix_connector_provisioning_requests_status_id",
                schema: "portal",
                table: "connector_provisioning_requests",
                column: "status_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connector_provisioning_requests",
                schema: "portal");

            migrationBuilder.DropTable(
                name: "connector_provisioning_bases",
                schema: "portal");

            migrationBuilder.DropTable(
                name: "connector_provisioning_request_statuses",
                schema: "portal");
        }
    }
}
