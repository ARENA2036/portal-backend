# BE-293 Portal integration test steps

This guide covers the combined OIDC claim-name and Connector provisioning changes on `feat/BE-293-portal-integration`. Use an isolated test deployment and test database. Do not use production identities or credentials. Keep bearer tokens in an approved secret store or API client's protected credential store; do not paste them into tickets, chat, shell history, or test output.

## 1. Prepare the test environment

1. Build and deploy the integration branch to the designated test environment using its normal release process.
2. Apply the `BE293ConnectorProvisioningRequests` database migration with the environment's approved migration job before starting the updated services. Do not run the migration against a shared or production database.
3. Confirm the Portal Administration Service and Registration Service use the same migrated test database and expected Central IDP issuer/audience configuration.
4. Keep `ApplicationActivation:ConnectorProvisioningEnabled` disabled for the first smoke test. The DEV-only `ConnectorProvisioning:DevOperatorRequestsEnabled` setting is `false` by default and should remain false unless the test owner explicitly chooses the operator-request path.
5. Prepare an application in the test fixture with a valid BPN and required registration state. Record its application/company identifiers through approved test tooling.

## 2. Verify Portal OIDC login and Dashboard identity resolution

1. Open the test Portal over HTTPS and authenticate through the configured Central IDP / federated Shared IDP flow with an already-approved test user.
2. In a local, access-controlled browser inspection, verify the fresh access token has the expected issuer, audience, non-empty `sub`, and expected Portal role. Do not copy or export the token.
3. Confirm the Portal Administration API's deployed JWT bearer configuration preserves the standard OIDC claim names (`MapInboundClaims = false`). This lets the identity handler resolve the Portal identity from `sub`; signature, issuer, audience, lifetime, and role validation remain enabled.
4. Load `/home` and verify the authenticated company dashboard request `GET /api/administration/companydata/ownCompanyDetails` returns `200` for a user with the intended company association and permission.
5. Verify an unauthenticated request is rejected and a token with the wrong issuer/audience is rejected. Do not treat a manually edited or unsigned token as a valid test.

## 3. Exercise normal Connector provisioning

This is the preferred path for the first end-to-end test.

1. With `ApplicationActivation:ConnectorProvisioningEnabled=true` only in the isolated test environment, confirm a valid application through the normal Portal application workflow.
2. In the same database unit of work as application confirmation, verify exactly one `REQUESTED` provisioning request is written for the company. Repeating activation or processing an existing live request must not create a duplicate.
3. Authenticate as the designated technical provisioning client with the `connector_provisioner` role and call `GET /api/administration/connector-provisioning/requests?status=REQUESTED`. Expect `200` and the request's IDs, BPN, basis, application/company status, and optional approver identifier. Do not log the response if it includes test personal data.
4. Report progress using `PUT /api/administration/connector-provisioning/requests/{requestId}/status` with one of the allowed transitions: `ACKNOWLEDGED`, `PROVISIONING`, `READY`, `FAILED`, or `REJECTED`, as appropriate. Status reports require both `connector_provisioner` and a valid Portal identity because final connector changes are audited.
5. For `READY`, provide the deployed test connector's absolute `http` or `https` URL (maximum 255 characters). Expect `204`; verify one active `CONNECTOR_AS_A_SERVICE` connector is registered against the application company and the request references it.
6. Repeat the same status once to check idempotence (expect `204` without a duplicate connector). Test an invalid transition (expect `409`) and a missing/invalid connector URL for `READY` (expect `400`) using disposable fixtures.
7. If testing teardown, report `DELETED` only through an allowed transition. Verify the associated connector becomes inactive. Do not directly delete or edit database rows.

## 4. Optional DEV operator-request path

Use this only if the test requires an application that has not reached the normal confirmed-activation path.

1. In the isolated DEV deployment only, set `ConnectorProvisioning:DevOperatorRequestsEnabled=true` after the test owner approves this behavior. Keep it false elsewhere.
2. Authenticate as an operator with `approve_new_partner` and a valid Portal identity.
3. Call `POST /api/administration/connector-provisioning/requests/dev` with `{"applicationId":"<test-application-guid>"}`. Expect `201` and a request location/ID.
4. Verify the application is `SUBMITTED` or `CONFIRMED`, has a BPN, and the company has no other live request. A duplicate live request returns `409`; the approver identity is taken from the authenticated caller, not the body.
5. Continue at step 3 in the normal provisioning flow. Disable the DEV operator-request setting after the test.

## 5. Authorization and validation checks

- A caller without `connector_provisioner` cannot list requests or report status (`403`).
- A caller without `approve_new_partner` or a valid identity cannot create a DEV request (`403`).
- The DEV request endpoint stays disabled by default and returns `403` when its setting is false.
- Unknown request/application identifiers return `404`; an existing live request, invalid application state, missing BPN, or disallowed transition returns `409`.
- `READY` requires a valid absolute HTTP(S) connector URL; malformed or missing values return `400`.
- Do not include access tokens, client secrets, private keys, or database connection strings in requests, test output, or evidence.

## 6. Local regression commands

From the repository root:

```sh
dotnet build src --configuration Release
dotnet format src --verify-no-changes --no-restore
dotnet test tests/framework/Framework.Web.Tests/Framework.Web.Tests.csproj --configuration Release
dotnet test tests/web/Web.Identity.Tests/Web.Identity.Tests.csproj --configuration Release
dotnet test tests/administration/Administration.Service.Tests/Administration.Service.Tests.csproj --configuration Release
dotnet test tests/portalbackend/PortalBackend.DBAccess.Tests/PortalBackend.DBAccess.Tests.csproj --configuration Release
dotnet test tests/registration/ApplicationActivation.Library.Tests/ApplicationActivation.Library.Tests.csproj --configuration Release
```

The Administration Service and DB access projects include container-backed tests; run them with Docker available. A local pass does not replace the isolated deployed Portal login and provisioning checks above.
