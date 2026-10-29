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
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Tests.Setup;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Entities;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Xunit.Extensions.AssemblyFixture;

namespace Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Tests;

public class ConnectorProvisioningRequestRepositoryTests(TestDbFixture testDbFixture) : IAssemblyFixture<TestDbFixture>
{
    // seeded (company_applications.unittest.json / companies.unittest.json): SUBMITTED application of a PENDING company
    private readonly Guid _applicationId = new("6b2d1263-c073-4a48-bfaf-704dc154ca9e");
    private readonly Guid _companyId = new("41fd2ab8-71cd-4546-9bef-a388d91b2542");

    private async Task<(ConnectorProvisioningRequestRepository, PortalDbContext)> CreateSut()
    {
        var context = await testDbFixture.GetPortalDbContext();
        return (new ConnectorProvisioningRequestRepository(context), context);
    }

    [Fact]
    public async Task CreateRequest_AddsARequestedRowToTheUnitOfWork()
    {
        var (sut, context) = await CreateSut();
        var now = DateTimeOffset.UtcNow;
        var approver = Guid.NewGuid();

        var result = sut.CreateRequest(_companyId, _applicationId, "BPNL00000003LLHA", ConnectorProvisioningBasisId.DEV_OPERATOR_APPROVED, approver, now);

        var entry = context.ChangeTracker.Entries().Should().ContainSingle().Which;
        entry.State.Should().Be(EntityState.Added);
        entry.Entity.Should().BeOfType<ConnectorProvisioningRequest>().Which.Should().Match<ConnectorProvisioningRequest>(x =>
            x.Id == result.Id && x.CompanyId == _companyId && x.ApplicationId == _applicationId &&
            x.StatusId == ConnectorProvisioningRequestStatusId.REQUESTED && x.BasisId == ConnectorProvisioningBasisId.DEV_OPERATOR_APPROVED &&
            x.ApprovedBy == approver && x.DateCreated == now && x.Version != Guid.Empty);
    }

    [Fact]
    public async Task GetApplicationData_ReturnsCompanyStatusAndBpn()
    {
        var (sut, _) = await CreateSut();

        var result = await sut.GetApplicationData(_applicationId);

        result.Should().NotBeNull();
        result!.CompanyId.Should().Be(_companyId);
        result.ApplicationStatusId.Should().Be(CompanyApplicationStatusId.SUBMITTED);
        result.BusinessPartnerNumber.Should().Be("BPNL00000003LLHA");
        (await sut.GetApplicationData(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Queries_OnAnEmptyOutbox_ReturnNothing()
    {
        var (sut, _) = await CreateSut();

        (await sut.HasLiveRequest(_companyId)).Should().BeFalse();
        (await sut.GetRequestStatusData(Guid.NewGuid())).Should().BeNull();
        (await sut.GetRequestsByStatus(ConnectorProvisioningRequestStatusId.REQUESTED).ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AttachAndModifyRequest_TracksTheModificationAndBumpsTheVersion()
    {
        var (sut, context) = await CreateSut();
        var id = Guid.NewGuid();
        var version = Guid.NewGuid();

        sut.AttachAndModifyRequest(id, version, r => r.StatusId = ConnectorProvisioningRequestStatusId.ACKNOWLEDGED);

        var entry = context.ChangeTracker.Entries<ConnectorProvisioningRequest>().Should().ContainSingle().Which;
        entry.State.Should().Be(EntityState.Modified);
        entry.Entity.StatusId.Should().Be(ConnectorProvisioningRequestStatusId.ACKNOWLEDGED);
        entry.Property(x => x.Version).OriginalValue.Should().Be(version);           // optimistic concurrency check value
        entry.Entity.Version.Should().NotBe(version);
    }

    [Fact]
    public async Task ReadyConnectorInsert_IsAuditedWithTheCallerAndRollsBackAsOneUnit()
    {
        // The READY path inserts an audited connectors row: SaveChanges stamps LastEditorId from the caller's identity
        // (FakeIdentityService here; the controller's technical user live). Run in a transaction that is rolled back,
        // so the shared fixture database is unchanged.
        var (_, context) = await CreateSut();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var connector = new ConnectorsRepository(context).CreateConnector("EDC BPNL0000000000ZZ", "DE",
            "http://edc-bpnl0000000000zz-controlplane.be293-connectors.svc.cluster.local:8084/api/v1/dsp", c =>
            {
                c.TypeId = ConnectorTypeId.CONNECTOR_AS_A_SERVICE;
                c.StatusId = ConnectorStatusId.ACTIVE;
                c.ProviderId = _companyId;
            });

        await context.SaveChangesAsync();

        var stored = await context.Connectors.AsNoTracking().SingleAsync(x => x.Id == connector.Id);
        stored.LastEditorId.Should().Be(new Guid("ac1cf001-7fbc-1f2f-817f-bce058020001"));
        stored.DateLastChanged.Should().NotBeNull();

        await transaction.RollbackAsync();
        context.ChangeTracker.Clear();
        (await context.Connectors.AnyAsync(x => x.Id == connector.Id)).Should().BeFalse();
    }
}
