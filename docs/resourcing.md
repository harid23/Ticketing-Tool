Resource Request and Allocation Management System
1. User Roles and Permissions

The application must support only the following three roles:

PM (Project Manager)
ASDM (Manager)
Admin

ASDM and Manager refer to the same role and must have identical permissions throughout the application. There must not be a separate Manager role.

Role-Based Permissions
Feature	PM	ASDM (Manager)	Admin
Create Resource Request	Yes	Yes	Yes
View Available Resources	Yes	Yes	Yes
Show Interest in Resources	No	Yes	No
View My Owned Tickets	Yes, according to access rules	Yes	No; use Available Resources and ticket details
Update Allocation Status	No	Yes, for owned tickets	According to existing admin authorization rules
Permission Rules
All three roles must be able to create resource requests.
All three roles can access the Available Resources page and view resource information.
Only ASDM users can see and use the Show Interest button.
PM users can view resources but must never see or invoke the Show Interest action.
Admin users can create requests and access the application according to their existing administrative permissions, but must not be given the ASDM interest action by default.
Only authorized ASDM users can update allocation statuses for tickets within their ownership or cluster context.
Enforce all permissions in both the frontend and backend APIs. Hiding UI controls alone is not sufficient.
2. Sidebar Navigation

The application shows role-appropriate navigation pages:

Create Request — Available to PM, ASDM, and Admin.
Available Resources — Viewable by all three roles, with the Show Interest action available only to ASDM.
My Owned Tickets — Available to PM and ASDM for allocation tickets associated with the user's authorized ownership or cluster context. Each parent release request is listed once, even when multiple clusters are interested.
All Release Requests — Available to Admin for one row per parent request; selecting a ticket opens its detailed view and parallel per-cluster allocation flow.
Admins do not have owned cluster allocation tickets. The owned-allocation API denies Admin access; Admin can review and manage authorized allocations from the release-request detail view.
3. SLA and Resource Allocation Rules
Every resource request has a 14-calendar-day SLA starting from the request creation date.
Expressing interest does not restart the SLA.
If an ASDM user expresses interest on Day 3, the interview and allocation process must be completed within the remaining 11 days.
Multiple ASDM users or clusters can express interest in the same resource and process allocation in parallel.
Each cluster must have its own allocation ticket and status for the resource.
4. My Owned Tickets and Status Management

Authorized ASDM users must be able to update their allocation ticket status using a dropdown with the following values:

Interview Scheduled
Waiting for Results
Selected
Rejected
Ticket Closure Rules

When a cluster rejects a resource:

Close only that cluster's allocation ticket.
Allow other interested clusters to continue their allocation process.

When a cluster selects a resource:

Mark that cluster's ticket as Selected.
Automatically close all competing active allocation tickets for the same resource.
Prevent other clusters from selecting the same resource.

Status changes must remain isolated between clusters until a resource is selected. Concurrent selection attempts must be handled atomically at the backend so that only one cluster can successfully allocate a resource.

5. Implementation Requirement

Review the existing application before making changes. Reuse the current architecture, database schema, authentication, and components wherever possible.

Implement the three pages, role-based permissions, 14-day SLA tracking, parallel allocation workflow, status dropdown, and ticket closure rules without rebuilding the entire application unnecessarily.

The final implementation must support exactly three roles: PM, ASDM, and Admin, with ASDM and Manager treated as the same role throughout the application.





Resource Request and Allocation Management System
1. Project Overview

I have an existing base application, but the complete business logic and workflows described below have not yet been implemented.

The goal is to enhance the existing application by implementing a role-based resource request and allocation management system with a sidebar navigation containing three main pages.

The implementation must preserve the existing application structure, design patterns, and functionality wherever possible. Before making changes, review the current codebase to understand the existing architecture, components, database schema, authentication, and role management.

2. User Roles and Permissions

The application must support the following four roles:

PM (Project Manager)
ASDM
Manager
Admin
Role-based access rules
All four roles must be able to create resource requests.
ASDM and Manager roles must be able to express interest in available resources.
PM users must be able to access and view available resources but must not see or access the "Show Interest" action.
Admin users must be able to create requests. Other administrative permissions should follow the existing application's authorization model unless additional requirements are defined.
Access control must be enforced at both the UI and backend/API levels. Hiding a button alone is not sufficient.
3. Sidebar Navigation and Pages

The application must provide a simple, consistent sidebar navigation with the following three pages.

Page 1: Create Request

Provide a page where PM, ASDM, Manager, and Admin users can create resource requests.

Requirements:

Provide a request creation form using the existing application's data model wherever possible.
Capture the required information for a resource request.
Validate mandatory fields before submission.
Save the request and make it available in the resource allocation workflow.
Start the 14-day SLA when the request is created.
Display relevant request details, creation date, SLA deadline, and current status wherever applicable.
Page 2: Available Resources

Provide a table-format view of resources that are available for allocation.

Requirements:

Display available resources in a clear, searchable, and filterable table.
Show relevant resource information, request details, and SLA-related information.
Allow eligible users to express interest in a resource.
Managers and ASDM users must see the "Show Interest" action.
PM users must be able to view the resources but must never see the "Show Interest" button.
Admin users can view available resources according to the existing authorization rules.

When a Manager or ASDM user expresses interest in a resource, the system must create an association between that resource and the interested user's cluster or ownership context.

Multiple clusters must be allowed to express interest in the same resource simultaneously. Expressing interest by one cluster must not prevent other eligible clusters from processing the same resource.

Page 3: My Owned Tickets

Provide a landing page where users can view and manage resource allocation tickets associated with their cluster or ownership context.

Requirements:

Display all relevant tickets for resources in which the current user's cluster has expressed interest.
Show resource details, interested cluster information, current allocation status, SLA deadline, and remaining SLA time.
Allow authorized Managers to update the allocation status using a dropdown.
Support parallel allocation workflows for the same resource across multiple clusters.
Clearly distinguish active, completed, rejected, and closed tickets.
Ensure that ticket updates for one cluster do not unintentionally affect another cluster's workflow.

The page must provide a clear overview of the resource allocation lifecycle and the actions available to the current user.

4. SLA Management: 14-Day Rule

Every resource request must have a total SLA period of 14 calendar days, starting from the request creation date.

The SLA must continue running regardless of when a Manager or ASDM expresses interest in the resource.

SLA example
Day 1: A resource request is created, and the 14-day SLA begins.
Day 3: An ASDM user expresses interest in the resource.
Remaining SLA: 11 days.
Within the remaining 11 days: The interview must be scheduled, conducted, and the allocation decision completed.

The system must not restart or extend the SLA when a cluster expresses interest in a resource. Once the initial 14-day deadline passes, Admin may approve one additional 14-day extension for the ASDM manager to finish the allocation results. Without approval, the request stays open for an Admin or related ASDM manager to close with a final note. If an approved extension expires without a selection, the SLA monitor closes the request.

SLA requirements
Store the request creation timestamp and SLA deadline.
Calculate the remaining SLA time dynamically.
Display the remaining time or deadline on relevant pages.
Ensure that every interested cluster works within the current request deadline, including an explicitly approved extension.
Provide appropriate visual indicators for approaching or breached deadlines.
Keep SLA tracking consistent across all clusters associated with the same resource request.

Unless the existing business rules specify otherwise, the SLA should use calendar days rather than working days.

5. Resource Allocation Status Management

On the My Owned Tickets page, authorized Managers must be able to update the status of a resource allocation ticket using a dropdown.

The dropdown must support the following statuses:

Interview Scheduled
Waiting for Results
Selected
Rejected

The system must record status changes against the appropriate resource, request, and cluster association.

Status behavior

Interview Scheduled

Indicates that an interview has been scheduled for the resource within the remaining SLA period.

Waiting for Results

Indicates that the interview has been completed and the allocation decision is pending.

Selected

Indicates that the resource has been selected by the current cluster.
Once a resource is selected by one cluster, the system must close all other active allocation tickets for the same resource associated with competing clusters.
The resource must no longer be available for allocation through those competing tickets.
The selection and closure operations must be handled consistently to prevent multiple clusters from selecting the same resource.

Rejected

Indicates that the resource has been rejected by the current cluster.
Only the ticket belonging to the rejecting cluster must be closed.
Other clusters that have expressed interest in the same resource must be allowed to continue their allocation processes.
Rejection by one cluster must not make the resource unavailable to other interested clusters.
6. Parallel Resource Allocation Workflow

The system must support parallel allocation processing when multiple clusters express interest in the same resource.

Example scenario

Assume that three clusters—Cluster A, Cluster B, and Cluster C—express interest in the same resource.

Scenario A: One cluster rejects the resource

Cluster A updates the status to Rejected.
Cluster A's ticket is closed.
Cluster B and Cluster C can continue scheduling interviews and processing the resource.
The resource remains available to the other interested clusters.

Scenario B: One cluster selects the resource

Cluster B updates the status to Selected.
Cluster B's ticket is marked as selected.
All other active tickets for the same resource, including Cluster A's and Cluster C's tickets, are automatically closed.
The resource is considered allocated, and no competing cluster can continue the allocation process for that resource.

Scenario C: Multiple clusters process the resource simultaneously

All interested clusters can independently schedule interviews and update their allocation statuses.
Changes made by one cluster must not overwrite another cluster's status unless a defined business rule explicitly requires it.
If multiple clusters attempt to select the same resource at approximately the same time, the system must allow only one successful selection.
The winning selection must be enforced atomically at the backend/database level.
7. Ticket Ownership and Closure Rules

The system must distinguish between a resource request and the individual allocation tickets created when different clusters express interest in that resource.

Each allocation ticket must maintain its own:

Resource reference
Original request reference
Cluster or ownership reference
Interested user reference
Current allocation status
Interview and decision details, where applicable
Creation and update timestamps
SLA deadline reference

The following rules are mandatory:

Rejecting a resource closes only the rejecting cluster's allocation ticket.
Selecting a resource closes all competing active allocation tickets for that same resource.
Closing one cluster's ticket must not close the original resource request unless the business rules explicitly require it.
Ticket status changes must be isolated by cluster until a resource is selected.
A resource must not be selected by more than one cluster.
Closed tickets must not be reopened or modified through ordinary status updates unless an authorized reopening workflow is implemented.
8. UI and User Experience Requirements

The application should provide a simple, consistent interface that fits the existing base application.

Sidebar
Create Request
Available Resources
My Owned Tickets
Available Resources table

Include relevant columns such as:

Resource ID
Resource Name
Skills or Technology
Experience
Availability
Request Details
SLA Deadline
Action

The Action column must display "Show Interest" only to eligible Manager and ASDM users.

My Owned Tickets table

Include relevant columns such as:

Ticket ID
Resource Details
Request ID
Cluster
Interest Date
Interview Status
Current Allocation Status
SLA Deadline
Remaining SLA
Actions

The status dropdown must be displayed only to users authorized to manage the corresponding ticket.

The interface should provide suitable loading states, validation messages, success notifications, error handling, and confirmation prompts for significant actions.

9. Backend and Data Integrity Requirements

The implementation must ensure that the business rules are enforced consistently, even when requests are submitted directly through APIs or multiple users update tickets simultaneously.

Requirements:

Enforce role-based authorization on all relevant endpoints.
Maintain a clear relationship between resource requests, resources, clusters, and allocation tickets.
Support multiple allocation tickets for the same resource across different clusters.
Prevent duplicate interest records for the same resource and cluster where inappropriate.
Ensure that selecting a resource and closing competing tickets occur within a single transaction or equivalent atomic operation.
Prevent conflicting selections caused by concurrent updates.
Preserve an audit trail of important status changes, including the user, timestamp, previous status, and new status.
Handle SLA deadlines consistently using the application's established timezone and timestamp conventions.

The implementation should reuse the existing database schema and APIs where appropriate. Any required schema changes or migrations must be identified before implementation.

10. Implementation Approach

The existing base application must be reviewed before implementing these requirements.

Follow this sequence:

Inspect the existing frontend, backend, database schema, authentication, role management, and current resource request workflow.
Identify which parts of the three pages already exist and which are incomplete or missing.
Map the existing data model to the required request, resource, cluster, and allocation ticket relationships.
Implement role-scoped sidebar pages and their navigation.
Implement role-based visibility and backend authorization.
Implement resource interest registration and parallel allocation workflows.
Implement the 14-day SLA calculation and deadline tracking.
Implement allocation status updates and the required ticket closure rules.
Add appropriate database constraints, transaction handling, and audit logging.
Test the complete workflow for all four roles, including concurrent allocation attempts.
Verify that existing functionality remains unaffected.

Do not rebuild the application from scratch unless the existing architecture makes the required functionality impractical to implement.

11. Acceptance Criteria

The implementation will be considered complete when all the following conditions are satisfied:

All four roles can create resource requests.
Eligible users can view one row per release request in Available Resources; opening a ticket shows its detailed information and a separate allocation process flow for each interested cluster.
Manager and ASDM users can express interest in resources.
PM users can view resources but cannot see or invoke the interest action.
Multiple clusters can express interest in the same resource.
Every request follows the original 14-day SLA.
Expressing interest does not restart the SLA.
Authorized Managers can update allocation statuses through the My Owned Tickets page.
Rejecting a resource closes only the rejecting cluster's ticket.
Selecting a resource closes all competing active tickets for that resource.
Parallel processing does not allow one cluster's routine status updates to overwrite another cluster's progress.
Concurrent selection attempts cannot result in multiple successful allocations.
Role-based access restrictions are enforced by both the frontend and backend, including denying Admin access to My Owned Tickets.
The implementation integrates with the existing application rather than duplicating its core functionality.
12. Final Objective

Complete the existing application by implementing the three-page, role-based resource request and allocation workflow described above.

Prioritize correct business logic, clear ownership boundaries, accurate SLA tracking, and reliable parallel allocation handling. Maintain a simple user interface while ensuring that all authorization, status transition, ticket closure, and concurrency rules are enforced at the backend level.