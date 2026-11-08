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

using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.Framework.DateTimeProvider;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Processes.Library.Concrete.Entities;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Processes.Library.Enums;
using Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.BusinessLogic;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Models;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Entities;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Processes.ApplicationChecklist.Library;
using System.Collections.Immutable;
using Xunit;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.Tests;

public class IdentityHubCredentialAwaitBusinessLogicTests
{
    private static readonly Guid ApplicationId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    private const string Bpn = "BPNL000000000001";

    private readonly IApplicationRepository _applicationRepository = A.Fake<IApplicationRepository>();
    private readonly IIdentityHubService _identityHubService = A.Fake<IIdentityHubService>();
    private readonly IDateTimeProvider _dateTimeProvider = A.Fake<IDateTimeProvider>();
    private readonly IIdentityHubCredentialAwaitBusinessLogic _sut;

    public IdentityHubCredentialAwaitBusinessLogicTests()
    {
        var portalRepositories = A.Fake<IPortalRepositories>();
        A.CallTo(() => portalRepositories.GetInstance<IApplicationRepository>()).Returns(_applicationRepository);
        A.CallTo(() => _dateTimeProvider.OffsetNow).Returns(Now);

        A.CallTo(() => _applicationRepository.GetBpnlCredentialIformationByApplicationId(ApplicationId))
            .Returns((true, "did:web:holder", Bpn, (WalletInformation?)null));

        _sut = new IdentityHubCredentialAwaitBusinessLogic(
            portalRepositories,
            _identityHubService,
            _dateTimeProvider,
            Options.Create(new IdentityHubSettings { MaxCredentialWaitTimeInDays = 1, BpnCredentialType = "BpnCredential", MembershipCredentialType = "MembershipCredential" }),
            A.Fake<ILogger<IdentityHubCredentialAwaitBusinessLogic>>());
    }

    private static IApplicationChecklistService.WorkerChecklistProcessStepData Context(ProcessStepTypeId stepTypeId) =>
        new(ApplicationId, stepTypeId, ImmutableDictionary<ApplicationChecklistEntryTypeId, ApplicationChecklistEntryStatusId>.Empty, Enumerable.Empty<ProcessStepTypeId>());

    private void WaitingSince(ProcessStepTypeId stepTypeId, params DateTimeOffset[] dateCreated) =>
        A.CallTo(() => _applicationRepository.GetActiveProcessStepsDateCreated(ApplicationId, stepTypeId))
            .Returns(dateCreated.AsEnumerable());

    private void HubReports(HolderCredentialRequestState state) =>
        A.CallTo(() => _identityHubService.GetCredentialRequestStateAsync(Bpn, A<string>._, A<CancellationToken>._))
            .Returns(state);

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE)]
    public async Task WithinTheBudget_StaysInTodoAndTouchesNothing(ProcessStepTypeId stepTypeId)
    {
        // The callback is still expected to finalize this step; the handler must not compete with it.
        HubReports(HolderCredentialRequestState.Pending);
        WaitingSince(stepTypeId, Now.AddHours(-23));

        var result = await Execute(stepTypeId);

        result.StepStatusId.Should().Be(ProcessStepStatusId.TODO);
        result.ModifyChecklistEntry.Should().BeNull();
        result.ScheduleStepTypeIds.Should().BeNull();
        result.Modified.Should().BeFalse();
        result.ProcessMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ProcessStepTypeId.RETRIGGER_REQUEST_MEMBERSHIP_CREDENTIAL)]
    public async Task PastTheBudget_FailsAndOffersTheRetrigger(ProcessStepTypeId stepTypeId, ProcessStepTypeId expectedRetrigger)
    {
        HubReports(HolderCredentialRequestState.Pending);
        WaitingSince(stepTypeId, Now.AddDays(-1).AddSeconds(-1));

        var result = await Execute(stepTypeId);

        result.StepStatusId.Should().Be(ProcessStepStatusId.FAILED);
        result.ScheduleStepTypeIds.Should().ContainSingle().Which.Should().Be(expectedRetrigger);
        result.ProcessMessage.Should().Contain("within 1 day");
    }

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE)]
    public async Task ExactlyAtTheDeadline_KeepsWaiting(ProcessStepTypeId stepTypeId)
    {
        HubReports(HolderCredentialRequestState.Pending);
        WaitingSince(stepTypeId, Now.AddDays(-1));

        var result = await Execute(stepTypeId);

        result.StepStatusId.Should().Be(ProcessStepStatusId.TODO);
        result.ModifyChecklistEntry.Should().BeNull();
        result.ScheduleStepTypeIds.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task WithoutExactlyOneActiveStep_ThrowsConflict(int activeSteps)
    {
        HubReports(HolderCredentialRequestState.Pending);
        WaitingSince(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, Enumerable.Repeat(Now, activeSteps).ToArray());

        async Task Act() => await Execute(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE);

        var ex = await Assert.ThrowsAsync<ConflictException>(Act);
        ex.Message.Should().Contain(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE.ToString());
    }

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ProcessStepTypeId.REQUEST_MEMBERSHIP_CREDENTIAL)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ProcessStepTypeId.START_CLEARING_HOUSE)]
    public async Task WhenTheHubReportsIssued_AdvancesWithoutTheCallback(ProcessStepTypeId stepTypeId, ProcessStepTypeId expectedNextStep)
    {
        // The whole point of polling: the credential exists, so onboarding proceeds even though no
        // callback ever arrived. Note the deadline is not consulted - this is true at any elapsed time.
        HubReports(HolderCredentialRequestState.Issued);

        var result = await Execute(stepTypeId);

        result.StepStatusId.Should().Be(ProcessStepStatusId.DONE);
        result.ScheduleStepTypeIds.Should().ContainSingle().Which.Should().Be(expectedNextStep);
        result.Modified.Should().BeTrue();

        var entry = new ApplicationChecklistEntry(ApplicationId, ApplicationChecklistEntryTypeId.BPNL_CREDENTIAL, ApplicationChecklistEntryStatusId.IN_PROGRESS, Now);
        result.ModifyChecklistEntry.Should().NotBeNull();
        result.ModifyChecklistEntry!.Invoke(entry);
        entry.ApplicationChecklistEntryStatusId.Should().Be(ApplicationChecklistEntryStatusId.DONE);
    }

    [Fact]
    public async Task WhenTheHubReportsIssued_DoesNotEvenLookAtTheDeadline()
    {
        HubReports(HolderCredentialRequestState.Issued);

        await Execute(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE);

        A.CallTo(() => _applicationRepository.GetActiveProcessStepsDateCreated(A<Guid>._, A<ProcessStepTypeId>._))
            .MustNotHaveHappened();
    }

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ProcessStepTypeId.RETRIGGER_REQUEST_MEMBERSHIP_CREDENTIAL)]
    public async Task WhenTheHubReportsError_FailsImmediately(ProcessStepTypeId stepTypeId, ProcessStepTypeId expectedRetrigger)
    {
        // Terminal on the holder side, so there is nothing to wait for - fail now rather than at the
        // deadline. The status API exposes no errorDetail, so the message must not imply we know why.
        HubReports(HolderCredentialRequestState.Failed);

        var result = await Execute(stepTypeId);

        result.StepStatusId.Should().Be(ProcessStepStatusId.FAILED);
        result.ScheduleStepTypeIds.Should().ContainSingle().Which.Should().Be(expectedRetrigger);
        result.ProcessMessage.Should().Contain("reported").And.Contain("failed");
    }

    [Fact]
    public async Task WhenTheRequestIsNotFound_KeepsWaitingUntilTheDeadline()
    {
        // A 404 also covers "the participant context does not exist yet", so it must not fail the step on
        // its own - the deadline still bounds it.
        HubReports(HolderCredentialRequestState.NotFound);
        WaitingSince(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, Now.AddHours(-1));

        var result = await Execute(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE);

        result.StepStatusId.Should().Be(ProcessStepStatusId.TODO);
        result.ModifyChecklistEntry.Should().BeNull();
    }

    [Fact]
    public async Task PollsUsingTheConfiguredCredentialType()
    {
        // The type must be the one the Portal requested, since holderPid is derived from it.
        HubReports(HolderCredentialRequestState.Pending);
        WaitingSince(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, Now);

        await Execute(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE);

        A.CallTo(() => _identityHubService.GetCredentialRequestStateAsync(Bpn, "MembershipCredential", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    #region checklist entry on failure (BE-293)

    public static TheoryData<ProcessStepTypeId, ApplicationChecklistEntryTypeId, ProcessStepTypeId, HolderCredentialRequestState, string> TerminalFailures => new()
    {
        { ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.BPNL_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL, HolderCredentialRequestState.Failed, "The IdentityHub reported the BPN credential request as failed" },
        { ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.MEMBERSHIP_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_MEMBERSHIP_CREDENTIAL, HolderCredentialRequestState.Failed, "The IdentityHub reported the Membership credential request as failed" },
        { ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.BPNL_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL, HolderCredentialRequestState.Pending, "No BPN credential was issued within 1 day(s)" },
        { ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.MEMBERSHIP_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_MEMBERSHIP_CREDENTIAL, HolderCredentialRequestState.Pending, "No Membership credential was issued within 1 day(s)" },
        { ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.BPNL_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL, HolderCredentialRequestState.NotFound, "No BPN credential was issued within 1 day(s)" },
    };

    [Theory]
    [MemberData(nameof(TerminalFailures))]
    public async Task OnTerminalFailureOrTimeout_SetsTheChecklistEntryToFailedWithAComment(
        ProcessStepTypeId stepTypeId, ApplicationChecklistEntryTypeId entryTypeId, ProcessStepTypeId expectedRetrigger, HolderCredentialRequestState state, string expectedMessage)
    {
        // Failed is terminal at any time; Pending and NotFound only fail once past the deadline.
        HubReports(state);
        WaitingSince(stepTypeId, Now.AddDays(-1).AddSeconds(-1));
        var entry = new ApplicationChecklistEntry(ApplicationId, entryTypeId, ApplicationChecklistEntryStatusId.IN_PROGRESS, Now);

        var result = await Execute(stepTypeId);

        result.StepStatusId.Should().Be(ProcessStepStatusId.FAILED);
        result.ScheduleStepTypeIds.Should().ContainSingle().Which.Should().Be(expectedRetrigger);
        result.SkipStepTypeIds.Should().BeNull();
        result.Modified.Should().BeTrue();
        result.ProcessMessage.Should().Be(expectedMessage);
        result.ModifyChecklistEntry.Should().NotBeNull();
        result.ModifyChecklistEntry!.Invoke(entry);
        entry.ApplicationChecklistEntryStatusId.Should().Be(ApplicationChecklistEntryStatusId.FAILED);
        entry.Comment.Should().Be(expectedMessage);
    }

    [Theory]
    [MemberData(nameof(TerminalFailures))]
    public async Task TheFailureCommentCarriesNoHolderData(
        ProcessStepTypeId stepTypeId, ApplicationChecklistEntryTypeId entryTypeId, ProcessStepTypeId expectedRetrigger, HolderCredentialRequestState state, string expectedMessage)
    {
        // The comment is shown to operators in the Portal UI: no BPN, DID or configured credential type.
        HubReports(state);
        WaitingSince(stepTypeId, Now.AddDays(-2));
        var entry = new ApplicationChecklistEntry(ApplicationId, entryTypeId, ApplicationChecklistEntryStatusId.IN_PROGRESS, Now);

        var result = await Execute(stepTypeId);
        result.ModifyChecklistEntry!.Invoke(entry);

        expectedRetrigger.Should().NotBe(stepTypeId);
        expectedMessage.Should().NotBeEmpty();
        entry.Comment.Should().NotContain(Bpn).And.NotContain("did:").And.NotContain("BpnCredential").And.NotContain("MembershipCredential");
    }

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.BPNL_CREDENTIAL)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.MEMBERSHIP_CREDENTIAL)]
    public async Task WhenIssuedPastTheDeadline_StillAdvancesAndLeavesTheCommentAlone(ProcessStepTypeId stepTypeId, ApplicationChecklistEntryTypeId entryTypeId)
    {
        // Callback race: the credential was issued (the callback may have been lost or is still in flight).
        // ISSUED wins over the deadline, so the worker never overwrites a success with FAILED.
        HubReports(HolderCredentialRequestState.Issued);
        WaitingSince(stepTypeId, Now.AddDays(-30));
        var entry = new ApplicationChecklistEntry(ApplicationId, entryTypeId, ApplicationChecklistEntryStatusId.IN_PROGRESS, Now) { Comment = "previous" };

        var result = await Execute(stepTypeId);
        result.ModifyChecklistEntry!.Invoke(entry);

        result.StepStatusId.Should().Be(ProcessStepStatusId.DONE);
        entry.ApplicationChecklistEntryStatusId.Should().Be(ApplicationChecklistEntryStatusId.DONE);
        entry.Comment.Should().Be("previous");
    }

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.BPNL_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.MEMBERSHIP_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_MEMBERSHIP_CREDENTIAL)]
    public async Task AfterTheFailure_TheManualRetriggerIsAccepted(ProcessStepTypeId stepTypeId, ApplicationChecklistEntryTypeId entryTypeId, ProcessStepTypeId retriggerStepTypeId)
    {
        // The defect: the retrigger endpoint validates the entry against FAILED. With the entry left
        // IN_PROGRESS it answered 409 although the retrigger step had been scheduled.
        HubReports(HolderCredentialRequestState.Failed);
        var entry = new ApplicationChecklistEntry(ApplicationId, entryTypeId, ApplicationChecklistEntryStatusId.IN_PROGRESS, Now);

        var result = await Execute(stepTypeId);
        result.ModifyChecklistEntry!.Invoke(entry);
        var afterWorker = StateAfterTheWorker(entry, result.ScheduleStepTypeIds!.Single());

        var retrigger = () => afterWorker.ValidateApplicationChecklistData(ApplicationId, entryTypeId, [ApplicationChecklistEntryStatusId.FAILED], [ProcessStepStatusId.TODO]);

        retrigger.Should().NotThrow();
        afterWorker.ProcessSteps.Should().ContainSingle(step => step.ProcessStepTypeId == retriggerStepTypeId);
    }

    [Theory]
    [InlineData(ApplicationChecklistEntryTypeId.BPNL_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL)]
    [InlineData(ApplicationChecklistEntryTypeId.MEMBERSHIP_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_MEMBERSHIP_CREDENTIAL)]
    public void WithTheEntryStillInProgress_TheManualRetriggerIsRejected(ApplicationChecklistEntryTypeId entryTypeId, ProcessStepTypeId retriggerStepTypeId)
    {
        // Pins the state the old code produced (step FAILED, entry IN_PROGRESS): the gate refuses it.
        var entry = new ApplicationChecklistEntry(ApplicationId, entryTypeId, ApplicationChecklistEntryStatusId.IN_PROGRESS, Now);
        var stale = StateAfterTheWorker(entry, retriggerStepTypeId);

        var retrigger = () => stale.ValidateApplicationChecklistData(ApplicationId, entryTypeId, [ApplicationChecklistEntryStatusId.FAILED], [ProcessStepStatusId.TODO]);

        retrigger.Should().Throw<ConflictException>().WithMessage($"*{entryTypeId}*FAILED*");
    }

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.BPNL_CREDENTIAL)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ApplicationChecklistEntryTypeId.MEMBERSHIP_CREDENTIAL)]
    public async Task ALateCallbackAfterTheFailure_IsRejectedAndCannotReopenTheEntry(ProcessStepTypeId stepTypeId, ApplicationChecklistEntryTypeId entryTypeId)
    {
        // Callback race, worker first: the issuer callback validates against IN_PROGRESS and an active
        // await step. Both are gone once the worker has committed, so the callback gets 409 and the
        // operator recovers through the retrigger, which re-polls and sees ISSUED if it arrived late.
        HubReports(HolderCredentialRequestState.Failed);
        var entry = new ApplicationChecklistEntry(ApplicationId, entryTypeId, ApplicationChecklistEntryStatusId.IN_PROGRESS, Now);

        var result = await Execute(stepTypeId);
        result.ModifyChecklistEntry!.Invoke(entry);
        var afterWorker = StateAfterTheWorker(entry, result.ScheduleStepTypeIds!.Single());

        var callback = () => afterWorker.ValidateApplicationChecklistData(ApplicationId, entryTypeId, [ApplicationChecklistEntryStatusId.IN_PROGRESS], [ProcessStepStatusId.TODO]);

        callback.Should().Throw<ConflictException>();
        afterWorker.ProcessSteps.Should().NotContain(step => step.ProcessStepTypeId == stepTypeId);
    }

    /// <summary>
    /// What the callback and retrigger endpoints read after the worker committed the step result: the
    /// repository returns only TODO steps, so the FAILED await step is absent and the retrigger step is present.
    /// </summary>
    private static VerifyChecklistData StateAfterTheWorker(ApplicationChecklistEntry entry, ProcessStepTypeId scheduledStepTypeId)
    {
        var process = new Process(Guid.NewGuid(), ProcessTypeId.APPLICATION_CHECKLIST, Guid.NewGuid());
        return new VerifyChecklistData(
            true,
            process,
            [(entry.ApplicationChecklistEntryTypeId, entry.ApplicationChecklistEntryStatusId, entry.Comment)],
            [new ProcessStep<Process, ProcessTypeId, ProcessStepTypeId>(Guid.NewGuid(), scheduledStepTypeId, ProcessStepStatusId.TODO, process.Id, Now)]);
    }

    #endregion

    private Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> Execute(ProcessStepTypeId stepTypeId) =>
        stepTypeId == ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE
            ? _sut.AwaitBpnCredentialResponse(Context(stepTypeId), CancellationToken.None)
            : _sut.AwaitMembershipCredentialResponse(Context(stepTypeId), CancellationToken.None);
}
