# Workspace: Apigee API Management Project

## Project Overview
This project contains an Apigee API Management configuration codebase. Based on the underlying directory structure (`src/main/apigee/`), the repository is scaffolded to manage core Apigee components, including API Proxies, Shared Flows, and Environment-specific configurations. Currently, the repository serves as an empty skeleton layout ready for Apigee development.

**Key Architecture Components:**
*   `src/main/apigee/apiproxies/`: The primary directory for API proxy bundles. Proxies handle traffic routing, request/response transformations, and security enforcement.
*   `src/main/apigee/sharedflows/`: The directory for reusable shared flows, which encapsulate standard policies (like security, logging, error handling) across multiple proxies.
*   `src/main/apigee/environments/`: To hold environment-specific settings such as Target Servers, Key Value Maps (KVMs), caches, and environment configurations.
*   `src/tests/`: Designated directory for API tests (e.g., integration, BDD, unit testing).

## Building and Deploying
*(TODO: Update this section once specific build tools (e.g., Maven, `apigeecli`, GCP Cloud Build) are initialized in the project).*

Currently, as there are no package or build definitions (`pom.xml`, `package.json`, etc.), typical workflows involve one of the following:

*   **Deploying Proxies via Apigee CLI/Tooling:**
    ```bash
    # Placeholder for standard deployment scripts
    # apigeetool deployproxy -u <username> -p <password> -o <org> -e <env> -n <proxy-name> -d src/main/apigee/apiproxies/<proxy-name>
    ```
*   **Testing via Apickli or similar frameworks:**
    ```bash
    # Placeholder for test execution
    # npm test --prefix src/tests/
    ```

## Development Conventions

1.  **Code Organization:** Adhere strictly to the defined folder structure. Any new API proxy or shared flow must be created in its respective `apiproxies/` or `sharedflows/` directory.
2.  **Environment Parity:** Ensure environment-specific references (e.g., backend hostnames, credentials) are managed dynamically via the `environments/` directory or TargetServers rather than hardcoded in the proxies.
3.  **Testing Strategy:** Test-driven development is highly encouraged. Ensure any new Proxy logic or Shared Flow logic has corresponding integration test coverage located in the `src/tests/` folder.
4.  **Version Control:** Ensure large or sensitive artifacts (secrets, `.db` files, credentials) are excluded from the repository. Review `.gitignore` when it's established to ensure `.comp/` (which appears to contain SQLite indices) and similar local build artifacts are safely ignored.
