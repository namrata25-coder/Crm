# VisionCRM Mobile & Client App API Reference Specification

This document provides the complete API specification for all VisionCRM **App API** endpoints under `/api/v1/app/`. Every endpoint includes required **Headers**, request **Body** schemas with JSON examples, and success/error **Response** bodies.

---

## 1. Global Specifications

### Base URLs
- **Local Development**: `http://localhost:5000` or `https://localhost:5001`
- **Route Prefix**: `/api/v1/app` *(aliases under `/api/app` are also supported for key modules)*

### Standard Request Headers
| Header | Value | Description |
|---|---|---|
| `Content-Type` | `application/json` | Required for all `POST`, `PUT`, `PATCH` requests |
| `Accept` | `application/json` | Required to receive structured JSON responses |
| `Authorization` | `Bearer <access_token>` | Required on all protected endpoints |

### Standard Response Envelopes

#### 1. Success Response Envelope (`AppApiResponse<T>`)
```json
{
  "success": true,
  "message": "Operation completed successfully",
  "data": { ... }
}
```

#### 2. Error Response Envelope (`AppApiErrorResponse`)
```json
{
  "success": false,
  "message": "Error description message.",
  "errors": {}
}
```

#### 3. Standard HTTP Status Codes
- `200 OK`: Request succeeded.
- `201 Created`: Resource successfully created.
- `204 NoContent`: Request succeeded with empty body (e.g., logout).
- `400 BadRequest`: Invalid payload, missing fields, or validation failure.
- `401 Unauthorized`: Missing, invalid, or expired Bearer token.
- `403 Forbidden`: Insufficient role permissions or portal mismatch.
- `404 NotFound`: Requested resource ID does not exist.
- `409 Conflict`: Unique constraint violation or duplicate entry.
- `423 Locked`: Account locked due to repeated failed logins.
- `500 InternalServerError`: Server runtime error.

---

## 2. Authentication Module (`/api/v1/app/auth`)

---

### 2.1 User Login
Authenticates an authorized user or sales representative and issues an opaque Bearer token.

- **Method**: `POST`
- **Route**: `/api/v1/app/auth/login`
- **Rate Limit**: 10 requests / minute / IP

#### Request Headers
```http
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "email": "user@visioncrm.com",
  "password": "UserPassword123!",
  "deviceId": "Pixel-8-Pro-Android-14",
  "role": "user"
}
```

#### Response (200 OK)
```json
{
  "token": "dGhpcy1pcy1hLXNhbXBsZS10b2tlbg...",
  "tokenType": "Bearer",
  "expiresAt": "2026-10-20T01:30:00Z",
  "user": {
    "id": 2,
    "name": "Standard User",
    "email": "user@visioncrm.com",
    "role": "User"
  }
}
```

#### Response (401 Unauthorized)
```json
{
  "message": "Invalid email or password."
}
```

#### Response (403 Forbidden - Role Mismatch)
```json
{
  "message": "Admin is not allowed to log in to the mobile app. This app is for Sales Manager and other team roles only. Please use the web portal."
}
```

---

### 2.2 Dedicated Admin Login
Strictly verifies that the credentials belong to an actual Admin or Super Admin account in the database.

- **Method**: `POST`
- **Route**: `/api/v1/app/auth/admin-login`

#### Request Headers
```http
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "email": "admin@visioncrm.com",
  "password": "AdminPassword123!",
  "deviceId": "Admin-Workstation-Chrome"
}
```

#### Response (200 OK)
```json
{
  "token": "YWRtaW4tc2VjdXJpdHktdG9rZW4...",
  "tokenType": "Bearer",
  "expiresAt": "2026-10-20T01:30:00Z",
  "user": {
    "id": 1,
    "name": "Administrator",
    "email": "admin@visioncrm.com",
    "role": "Admin"
  }
}
```

---

### 2.3 Dedicated User Login
Strictly verifies that the credentials belong to an authorized User account. Rejects Admin accounts.

- **Method**: `POST`
- **Route**: `/api/v1/app/auth/user-login`

#### Request Headers
```http
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "email": "user@visioncrm.com",
  "password": "UserPassword123!",
  "deviceId": "Samsung-Galaxy-S24"
}
```

#### Response (200 OK)
```json
{
  "token": "dXNlci1hdXRoLXRva2Vu...",
  "tokenType": "Bearer",
  "expiresAt": "2026-10-20T01:30:00Z",
  "user": {
    "id": 2,
    "name": "Standard User",
    "email": "user@visioncrm.com",
    "role": "User"
  }
}
```

---

### 2.4 Get Current User Profile (`/me`)
Retrieves the profile of the authenticated user corresponding to the Bearer token.

- **Method**: `GET`
- **Route**: `/api/v1/app/auth/me`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "id": 2,
  "name": "Standard User",
  "email": "user@visioncrm.com",
  "role": "User"
}
```

---

### 2.5 Change Password
Changes password for the current user and invalidates all other active login sessions.

- **Method**: `POST`
- **Route**: `/api/v1/app/auth/change-password`

#### Request Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "oldPassword": "UserPassword123!",
  "newPassword": "NewStrongUserPassword456!"
}
```

#### Response (204 No Content)
Empty body on success.

---

### 2.6 Logout
Revokes the current Bearer token.

- **Method**: `POST`
- **Route**: `/api/v1/app/auth/logout`

#### Request Headers
```http
Authorization: Bearer <access_token>
```

#### Response (204 No Content)
Empty body on success.

---

### 2.7 Logout All Devices
Revokes all active tokens for the user across all mobile and web devices.

- **Method**: `POST`
- **Route**: `/api/v1/app/auth/logout-all`

#### Request Headers
```http
Authorization: Bearer <access_token>
```

#### Response (204 No Content)
Empty body on success.

---

## 3. Leads Module (`/api/v1/app/leads`)

---

### 3.1 List Leads
Retrieves all leads formatted for app consumption.

- **Method**: `GET`
- **Route**: `/api/v1/app/leads`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
[
  {
    "id": 1,
    "leadId": "LD-1001",
    "leadName": "Rajesh Kumar",
    "companyName": "Acme Infotech",
    "mobileNumber": "+91 98765 43210",
    "email": "rajesh@acmeinfo.com",
    "designation": "CTO",
    "location": "Mumbai",
    "source": "Website",
    "leadStatus": "In Progress",
    "assignedTo": "Ananya Sharma",
    "priority": "High",
    "createdDate": "2026-10-15",
    "assignedDate": "2026-10-15",
    "followUpDate": "2026-10-22",
    "remarks": "Interested in enterprise CRM package.",
    "callHistory": [],
    "followUpHistory": [],
    "requirements": [],
    "assignmentHistory": []
  }
]
```

---

### 3.2 Lead Counts Summary
Retrieves lead counts and distributions by status and priority.

- **Method**: `GET`
- **Route**: `/api/v1/app/leads/count` *(or `/counts`)*
- **Query Parameters**:
  - `assignedTo` *(optional, string)*: Filter metrics by assigned employee.

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "total": 45,
  "assigned": 38,
  "unassigned": 7,
  "byStatus": {
    "New": 12,
    "In Progress": 18,
    "Converted": 10,
    "Closed": 5
  },
  "byPriority": {
    "High": 15,
    "Medium": 22,
    "Low": 8
  }
}
```

---

### 3.3 Get Lead Details by ID
Retrieves full details for a lead by numeric ID or Lead Code (e.g., `LD-1001`).

- **Method**: `GET`
- **Route**: `/api/v1/app/leads/{id}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "id": 1,
  "leadId": "LD-1001",
  "leadName": "Rajesh Kumar",
  "companyName": "Acme Infotech",
  "mobileNumber": "+91 98765 43210",
  "email": "rajesh@acmeinfo.com",
  "designation": "CTO",
  "location": "Mumbai",
  "source": "Website",
  "leadStatus": "In Progress",
  "assignedTo": "Ananya Sharma",
  "priority": "High",
  "createdDate": "2026-10-15",
  "assignedDate": "2026-10-15",
  "followUpDate": "2026-10-22",
  "remarks": "Interested in enterprise CRM package.",
  "callHistory": [],
  "followUpHistory": [],
  "requirements": [],
  "assignmentHistory": []
}
```

---

### 3.4 Create Lead
Creates a new lead with automated ID generation (`LD-1001`, `LD-1002`, etc.) and optional assignment history tracking.

- **Method**: `POST`
- **Route**: `/api/v1/app/leads` *(or `/add`, `/create`)*

#### Request Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "leadName": "Vikram Malhotra",
  "companyName": "Solaris Energy Ltd",
  "mobileNumber": "+91 99887 76655",
  "email": "vikram@solarisenergy.com",
  "designation": "Director of Operations",
  "location": "Bengaluru",
  "source": "LinkedIn",
  "leadStatus": "New",
  "assignedTo": "Ananya Sharma",
  "priority": "High",
  "followUpDate": "2026-10-25",
  "remarks": "Met at Tech Expo. Requested demo."
}
```

#### Response (201 Created)
```json
{
  "success": true,
  "message": "Lead 'Vikram Malhotra' (LD-1005) created successfully.",
  "data": {
    "id": 5,
    "leadId": "LD-1005",
    "leadName": "Vikram Malhotra",
    "companyName": "Solaris Energy Ltd",
    "mobileNumber": "+91 99887 76655",
    "email": "vikram@solarisenergy.com",
    "designation": "Director of Operations",
    "location": "Bengaluru",
    "source": "LinkedIn",
    "leadStatus": "New",
    "assignedTo": "Ananya Sharma",
    "priority": "High",
    "createdDate": "2026-10-19",
    "assignedDate": "2026-10-19",
    "followUpDate": "2026-10-25",
    "remarks": "Met at Tech Expo. Requested demo.",
    "assignmentHistory": [
      {
        "id": 1,
        "assignedEmployee": "Ananya Sharma",
        "assignmentDate": "2026-10-19",
        "remarks": "Initial lead assignment upon creation"
      }
    ]
  }
}
```

---

### 3.5 Update Lead
Updates existing lead information. Automatically appends to `assignmentHistory` if `assignedTo` is modified.

- **Method**: `PUT`
- **Route**: `/api/v1/app/leads/{id}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "leadName": "Vikram Malhotra",
  "companyName": "Solaris Energy Ltd",
  "mobileNumber": "+91 99887 76655",
  "email": "vikram@solarisenergy.com",
  "designation": "Managing Director",
  "location": "Bengaluru",
  "source": "LinkedIn",
  "leadStatus": "In Progress",
  "assignedTo": "Rohan Verma",
  "priority": "High",
  "followUpDate": "2026-10-28",
  "remarks": "Product demo delivered. Preparing commercial proposal."
}
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Lead 'Vikram Malhotra' (LD-1005) updated successfully.",
  "data": { ... }
}
```

---

### 3.6 Delete Lead
Deletes a lead by numeric ID or Lead Code.

- **Method**: `DELETE`
- **Route**: `/api/v1/app/leads/{id}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Lead 'Vikram Malhotra' (LD-1005) deleted successfully.",
  "data": null
}
```

---

## 4. Projects Module (`/api/v1/app/projects`)

---

### 4.1 List Projects
Retrieves paginated projects with search, filter, and role data scoping (`OnlyAssigned`).

- **Method**: `GET`
- **Route**: `/api/v1/app/projects`
- **Query Parameters**:
  - `page` *(int, default: 1)*
  - `pageSize` *(int, default: 20, max: 100)*
  - `status` *(string)*: e.g., `Active`, `Completed`, `On Hold`
  - `projectType` *(string)*: e.g., `Direct Project`, `Internal Project`
  - `priority` *(string)*: `High`, `Medium`, `Low`
  - `search` *(string)*: Search text in ProjectName, ProjectCode, ClientName, Manager

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Projects retrieved successfully.",
  "data": {
    "page": 1,
    "pageSize": 20,
    "totalCount": 1,
    "totalPages": 1,
    "items": [
      {
        "id": 1,
        "projectCode": "PRJ-1001",
        "projectName": "Mobile App Redesign",
        "clientName": "Acme Infotech",
        "clientCompany": "Acme Corp",
        "projectType": "Direct Project",
        "description": "Cross-platform mobile application development.",
        "startDate": "2026-10-01",
        "deadline": "2026-11-30",
        "projectManager": "Ananya Sharma",
        "assignedMembers": "Ananya Sharma, Rohan Verma",
        "budget": 250000.00,
        "completedAmount": 100000.00,
        "pendingAmount": 150000.00,
        "progressPercent": 40,
        "priority": "High",
        "status": "Active",
        "notes": "Milestone 1 completed and invoiced.",
        "createdDate": "2026-10-01"
      }
    ]
  }
}
```

---

### 4.2 Get Project by ID
Retrieves detailed project record including relations to Milestones, Tasks, and Invoices.

- **Method**: `GET`
- **Route**: `/api/v1/app/projects/{id}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Project retrieved successfully.",
  "data": {
    "id": 1,
    "projectCode": "PRJ-1001",
    "projectName": "Mobile App Redesign",
    "budget": 250000.00,
    "milestones": [ ... ],
    "tasks": [ ... ],
    "invoices": [ ... ]
  }
}
```

---

### 4.3 Create Project
Creates a new project record with an auto-generated project code (e.g., `PRJ-1002`).

- **Method**: `POST`
- **Route**: `/api/v1/app/projects`

#### Request Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "projectName": "ERP Cloud Migration",
  "clientName": "Apex Logistics",
  "clientCompany": "Apex Global Ltd",
  "projectType": "Direct Project",
  "description": "Migration from on-premise ERP to cloud infrastructure.",
  "startDate": "2026-11-01",
  "deadline": "2027-02-28",
  "projectManager": "Ananya Sharma",
  "assignedMembers": "Ananya Sharma, Vikram Malhotra",
  "budget": 500000.00,
  "priority": "High",
  "status": "Active",
  "notes": "Kick-off meeting scheduled for next week."
}
```

#### Response (201 Created)
```json
{
  "success": true,
  "message": "Project 'ERP Cloud Migration' created successfully.",
  "data": {
    "id": 2,
    "projectCode": "PRJ-1002",
    "projectName": "ERP Cloud Migration",
    "budget": 500000.00,
    "status": "Active",
    "createdDate": "2026-10-19"
  }
}
```

---

### 4.4 Update Project
- **Method**: `PUT`
- **Route**: `/api/v1/app/projects/{id}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "projectName": "ERP Cloud Migration - Phase 1",
  "clientName": "Apex Logistics",
  "clientCompany": "Apex Global Ltd",
  "projectType": "Direct Project",
  "description": "Migration phase 1 updated scope.",
  "startDate": "2026-11-01",
  "deadline": "2027-03-15",
  "projectManager": "Ananya Sharma",
  "assignedMembers": "Ananya Sharma, Rohan Verma",
  "budget": 550000.00,
  "priority": "High",
  "status": "Active",
  "notes": "Added database replica testing."
}
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Project 'ERP Cloud Migration - Phase 1' updated successfully.",
  "data": { ... }
}
```

---

### 4.5 Delete Project
Deletes a project. Rejects with `409 Conflict` if the project has paid invoices to protect financial accounting records.

- **Method**: `DELETE`
- **Route**: `/api/v1/app/projects/{id}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Project 'ERP Cloud Migration - Phase 1' deleted successfully.",
  "data": null
}
```

---

## 5. Milestones Module (`/api/v1/app/milestones`)

---

### 5.1 List Milestones
Retrieves milestones with pagination, dependency status, and filtering.

- **Method**: `GET`
- **Route**: `/api/v1/app/milestones`
- **Query Parameters**:
  - `page` *(int, default: 1)*
  - `pageSize` *(int, default: 20)*
  - `projectId` *(int, optional)*
  - `status` *(string, optional)*
  - `search` *(string, optional)*

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Milestones retrieved successfully.",
  "data": {
    "page": 1,
    "pageSize": 20,
    "totalCount": 2,
    "totalPages": 1,
    "items": [
      {
        "id": 1,
        "projectId": 1,
        "projectName": "Mobile App Redesign",
        "projectCode": "PRJ-1001",
        "milestoneCode": "MLS-1001",
        "milestoneName": "UI/UX Prototype Design",
        "description": "Design wireframes and interactive prototypes.",
        "startDate": "2026-10-01",
        "dueDate": "2026-10-15",
        "amount": 75000.00,
        "completedAmount": 75000.00,
        "pendingAmount": 0.00,
        "progressPercent": 100,
        "assignedMembers": "Ananya Sharma",
        "priority": "High",
        "status": "Completed",
        "tasksCount": 3,
        "isLocked": false,
        "lockReason": "",
        "dependencyType": "Sequential Completion Required",
        "sequenceIndex": 1,
        "invoiceNumber": "INV-1001",
        "invoiceStatus": "Paid"
      }
    ]
  }
}
```

---

### 5.2 Project Milestone Dependencies
Retrieves strict sequential Finish-to-Start dependency hierarchy, prerequisite lock conditions, and invoice payment statuses for a project.

- **Method**: `GET`
- **Route**: `/api/v1/app/milestones/project-dependency/{projectId}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Project dependencies for project 1 retrieved successfully.",
  "data": {
    "project": {
      "id": 1,
      "projectCode": "PRJ-1001",
      "projectName": "Mobile App Redesign",
      "budget": 250000.00,
      "status": "Active",
      "totalMilestones": 3,
      "completedMilestones": 1
    },
    "milestones": [
      {
        "id": 1,
        "milestoneCode": "MLS-1001",
        "milestoneName": "UI/UX Prototype",
        "status": "Completed",
        "isLocked": false,
        "sequenceIndex": 1
      },
      {
        "id": 2,
        "milestoneCode": "MLS-1002",
        "milestoneName": "Frontend API Integration",
        "status": "In Progress",
        "isLocked": false,
        "sequenceIndex": 2
      },
      {
        "id": 3,
        "milestoneCode": "MLS-1003",
        "milestoneName": "QA & Production Deployment",
        "status": "Pending",
        "isLocked": true,
        "lockReason": "Locked: Prerequisite Milestone MLS-1002 is not yet completed.",
        "sequenceIndex": 3
      }
    ]
  }
}
```

---

### 5.3 Create Milestone
- **Method**: `POST`
- **Route**: `/api/v1/app/milestones`

#### Request Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "projectId": 1,
  "milestoneName": "Backend Microservice Integration",
  "description": "Implement authentication and lead processing services.",
  "startDate": "2026-10-20",
  "dueDate": "2026-11-10",
  "amount": 100000.00,
  "completedAmount": 0.00,
  "assignedMembers": "Rohan Verma",
  "priority": "High",
  "status": "Pending",
  "notes": "Dependent on database schema readiness."
}
```

#### Response (201 Created)
```json
{
  "success": true,
  "message": "Milestone 'Backend Microservice Integration' (MLS-1004) created successfully.",
  "data": { ... }
}
```

---

### 5.4 Update Milestone
- **Method**: `PUT`
- **Route**: `/api/v1/app/milestones/{id}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "projectId": 1,
  "milestoneName": "Backend Microservice Integration",
  "description": "Implement authentication and lead processing services.",
  "startDate": "2026-10-20",
  "dueDate": "2026-11-15",
  "amount": 100000.00,
  "completedAmount": 50000.00,
  "assignedMembers": "Rohan Verma",
  "priority": "High",
  "status": "In Progress",
  "notes": "50% completed."
}
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Milestone 'Backend Microservice Integration' (MLS-1004) updated successfully.",
  "data": { ... }
}
```

---

### 5.5 Quick Status Toggle
- **Method**: `PATCH`
- **Route**: `/api/v1/app/milestones/{id}/status`

#### Request Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "status": "Completed"
}
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Milestone 'Backend Microservice Integration' status updated to 'Completed'.",
  "data": { ... }
}
```

---

### 5.6 Delete Milestone
- **Method**: `DELETE`
- **Route**: `/api/v1/app/milestones/{id}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Milestone 'Backend Microservice Integration' (MLS-1004) deleted successfully.",
  "data": null
}
```

---

## 6. Work Dashboard, Statistics, Reports & Tasks (`/api/v1/app/work`)

---

### 6.1 Work Summary Metrics
Aggregates real-time project, milestone, task, and financial counts across the company or scoped user.

- **Method**: `GET`
- **Route**: `/api/v1/app/work/summary` *(or `/api/v1/app/work`, `/api/app/work`)*
- **Query Parameters**:
  - `assignedUser` *(string, optional)*
  - `projectId` *(int, optional)*
  - `from` *(string, date: YYYY-MM-DD, optional)*
  - `to` *(string, date: YYYY-MM-DD, optional)*

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Work summary retrieved successfully.",
  "data": {
    "totalProjects": 8,
    "activeProjects": 5,
    "completedProjects": 2,
    "onHoldProjects": 1,
    "overdueProjects": 0,
    "totalMilestones": 24,
    "pendingMilestones": 10,
    "inProgressMilestones": 8,
    "completedMilestones": 6,
    "overdueMilestones": 1,
    "totalTasks": 65,
    "assignedTasks": 58,
    "pendingTasks": 25,
    "inProgressTasks": 22,
    "completedTasks": 18,
    "overdueTasks": 2,
    "totalBudget": 1850000.00,
    "completedValue": 650000.00,
    "pendingValue": 1200000.00
  }
}
```

---

### 6.2 Work Statistics & Distributions
Returns breakdown of status distributions, priority distributions, and overall completion percentages.

- **Method**: `GET`
- **Route**: `/api/v1/app/work/stats` *(or `/api/v1/app/work/statistics`)*

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Work statistics retrieved successfully.",
  "data": {
    "summary": { ... },
    "taskStatusDistribution": {
      "Pending": 25,
      "In Progress": 22,
      "Completed": 18
    },
    "milestoneStatusDistribution": {
      "Pending": 10,
      "In Progress": 8,
      "Completed": 6
    },
    "projectStatusDistribution": {
      "Active": 5,
      "Completed": 2,
      "On Hold": 1
    },
    "taskPriorityDistribution": {
      "High": 20,
      "Medium": 35,
      "Low": 10
    },
    "completionRatePercent": 27.69
  }
}
```

---

### 6.3 Comprehensive Work Reports
Returns paginated report items including financial totals, logged timesheet hours, and invoices.

- **Method**: `GET`
- **Route**: `/api/v1/app/work/reports` *(or `/api/v1/app/work/report`)*
- **Query Parameters**:
  - `page` *(int, default: 1)*
  - `pageSize` *(int, default: 20)*
  - `projectId` *(int, optional)*
  - `status` *(string, optional)*
  - `assignedUser` *(string, optional)*
  - `from` *(string, optional)*
  - `to` *(string, optional)*

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Work reports retrieved successfully.",
  "data": {
    "summary": {
      "totalProjects": 8,
      "totalBudget": 1850000.00,
      "totalCompletedAmount": 650000.00,
      "totalPendingAmount": 1200000.00,
      "totalInvoicedAmount": 700000.00,
      "totalExpenses": 125000.00,
      "totalLoggedHours": 348.5
    },
    "report": {
      "page": 1,
      "pageSize": 20,
      "totalCount": 8,
      "totalPages": 1,
      "items": [
        {
          "id": 1,
          "projectCode": "PRJ-1001",
          "projectName": "Mobile App Redesign",
          "clientName": "Acme Infotech",
          "projectManager": "Ananya Sharma",
          "assignedMembers": "Ananya Sharma, Rohan Verma",
          "status": "Active",
          "priority": "High",
          "startDate": "2026-10-01",
          "deadline": "2026-11-30",
          "budget": 250000.00,
          "completedAmount": 100000.00,
          "pendingAmount": 150000.00,
          "progressPercent": 40,
          "milestonesCount": 3,
          "completedMilestonesCount": 1,
          "tasksCount": 12,
          "completedTasksCount": 5,
          "invoicesGrandTotal": 100000.00,
          "expensesTotal": 15000.00,
          "timesheetHoursTotal": 78.5
        }
      ]
    }
  }
}
```

---

### 6.4 Work Tasks List
- **Method**: `GET`
- **Route**: `/api/v1/app/work/tasks`
- **Query Parameters**:
  - `page` *(int, default: 1)*
  - `pageSize` *(int, default: 20)*
  - `projectId` *(int, optional)*
  - `milestoneId` *(int, optional)*
  - `status` *(string, optional)*
  - `priority` *(string, optional)*
  - `assignedUser` *(string, optional)*
  - `search` *(string, optional)*

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Work tasks retrieved successfully.",
  "data": {
    "page": 1,
    "pageSize": 20,
    "totalCount": 1,
    "totalPages": 1,
    "items": [
      {
        "id": 1,
        "projectId": 1,
        "projectName": "Mobile App Redesign",
        "projectCode": "PRJ-1001",
        "milestoneId": 1,
        "milestoneName": "UI/UX Prototype Design",
        "milestoneCode": "MLS-1001",
        "taskCode": "TSK-1001",
        "taskName": "Create Figma Component Library",
        "description": "Design core typography, colors, and button atoms.",
        "assignedMember": "Ananya Sharma",
        "startDate": "2026-10-01",
        "dueDate": "2026-10-05",
        "taskAmount": 25000.00,
        "completedAmount": 25000.00,
        "pendingAmount": 0.00,
        "progressPercent": 100,
        "priority": "High",
        "status": "Completed",
        "notes": "Reviewed and approved by client.",
        "createdDate": "2026-10-01"
      }
    ]
  }
}
```

---

### 6.5 Get Work Task by ID
- **Method**: `GET`
- **Route**: `/api/v1/app/work/tasks/{id}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Task retrieved successfully.",
  "data": {
    "id": 1,
    "projectId": 1,
    "taskCode": "TSK-1001",
    "taskName": "Create Figma Component Library",
    "assignedMember": "Ananya Sharma",
    "status": "Completed"
  }
}
```

---

## 7. Team Roles & Module Permissions (`/api/v1/app/team/roles`)

---

### 7.1 List All Roles
Retrieves all roles along with assigned active user counts.

- **Method**: `GET`
- **Route**: `/api/v1/app/team/roles`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Roles retrieved successfully.",
  "data": [
    {
      "id": 1,
      "roleName": "Super Admin",
      "description": "Full access to all system features",
      "isSystem": true,
      "dataAccess": "All",
      "assignedUsersCount": 1
    },
    {
      "id": 2,
      "roleName": "Admin",
      "description": "Full access to all features except critical settings",
      "isSystem": true,
      "dataAccess": "All",
      "assignedUsersCount": 2
    },
    {
      "id": 3,
      "roleName": "Sales Executive",
      "description": "Access to assigned leads, clients, and projects",
      "isSystem": false,
      "dataAccess": "OnlyAssigned",
      "assignedUsersCount": 5
    }
  ]
}
```

---

### 7.2 Get Role Details & Permissions Matrix
Retrieves detailed role definition, assigned members list, and the 12-module CRUD permission matrix.

- **Method**: `GET`
- **Route**: `/api/v1/app/team/roles/{roleName}`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Role details for 'Sales Executive' retrieved successfully.",
  "data": {
    "id": 3,
    "roleName": "Sales Executive",
    "description": "Sales Executive role configuration",
    "isSystem": false,
    "dataAccess": "OnlyAssigned",
    "permissions": [
      {
        "module": "Lead",
        "view": true,
        "create": true,
        "edit": true,
        "delete": false
      },
      {
        "module": "Project",
        "view": true,
        "create": false,
        "edit": false,
        "delete": false
      }
    ],
    "assignedUsers": [
      {
        "id": 5,
        "name": "Rohan Verma",
        "email": "rohan@visioncrm.com",
        "status": "Active"
      }
    ]
  }
}
```

---

### 7.3 Save Role & Permissions
Creates or updates a role and synchronizes module permissions across all users assigned to that role.

- **Method**: `POST`
- **Route**: `/api/v1/app/team/roles/save`

#### Request Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "roleName": "Field Sales Agent",
  "dataAccess": "OnlyAssigned",
  "permissions": [
    {
      "module": "Lead",
      "view": true,
      "create": true,
      "edit": true,
      "delete": false
    },
    {
      "module": "Task",
      "view": true,
      "create": true,
      "edit": true,
      "delete": false
    }
  ]
}
```

#### Response (201 Created or 200 OK)
```json
{
  "success": true,
  "message": "Role 'Field Sales Agent' created successfully.",
  "data": {
    "id": 6,
    "roleName": "Field Sales Agent",
    "dataAccess": "OnlyAssigned",
    "updatedAssignedUsersCount": 0
  }
}
```

---

## 8. Application Settings Module (`/api/v1/app/settings`)

---

### 8.1 Full Application Settings
Retrieves all statuses, sources, sanitized users, tax rates, and follow-up configuration in one API call.

- **Method**: `GET`
- **Route**: `/api/v1/app/settings`

#### Request Headers
```http
Authorization: Bearer <access_token>
Accept: application/json
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Application settings retrieved successfully.",
  "data": {
    "statuses": ["New", "Contacted", "In Progress", "Converted", "Closed"],
    "sources": ["Website", "LinkedIn", "Referral", "Cold Call", "Exhibition"],
    "users": [
      {
        "id": 1,
        "name": "Administrator",
        "email": "admin@visioncrm.com",
        "role": "Admin",
        "status": "Active"
      }
    ],
    "taxes": [
      {
        "id": 1,
        "taxName": "GST 18%",
        "taxType": "GST",
        "taxRate": 18.00,
        "status": "Active"
      }
    ],
    "followUp": {
      "id": 1,
      "defaultReminderDaysBefore": 1,
      "enableEmailReminders": true,
      "enableSmsReminders": false,
      "defaultFollowUpType": "Call",
      "autoEscalateAfterDaysOverdue": 3
    }
  }
}
```

---

### 8.2 Lead Statuses
- **GET `/api/v1/app/settings/statuses`**: Returns list of all status options.
- **POST `/api/v1/app/settings/statuses`**: Adds a new status option.

#### Request Body (POST Status)
```json
{
  "value": "Under Negotiation"
}
```

#### Response (201 Created)
```json
{
  "success": true,
  "message": "Status 'Under Negotiation' added successfully.",
  "data": {
    "id": 6,
    "value": "Under Negotiation"
  }
}
```

- **DELETE `/api/v1/app/settings/statuses/{idOrName}`**: Deletes a status option.

---

### 8.3 Lead Sources
- **GET `/api/v1/app/settings/sources`**: Returns list of all lead sources.
- **POST `/api/v1/app/settings/sources`**: Adds a new source option.

#### Request Body (POST Source)
```json
{
  "value": "Trade Show 2026"
}
```

- **DELETE `/api/v1/app/settings/sources/{idOrName}`**: Deletes a source option.

---

### 8.4 User Management (`/api/v1/app/settings/users`)

#### Create New User
- **Method**: `POST`
- **Route**: `/api/v1/app/settings/users`

#### Request Headers
```http
Authorization: Bearer <access_token>
Content-Type: application/json
Accept: application/json
```

#### Request Body
```json
{
  "name": "Kavita Singhania",
  "email": "kavita@visioncrm.com",
  "role": "Sales Executive",
  "password": "KavitaPassword123!",
  "status": "Active",
  "modulePermissions": "Lead,Project,Task"
}
```

#### Response (201 Created)
```json
{
  "success": true,
  "message": "User 'Kavita Singhania' created successfully.",
  "data": {
    "id": 8,
    "name": "Kavita Singhania",
    "email": "kavita@visioncrm.com",
    "role": "Sales Executive",
    "status": "Active"
  }
}
```

#### Update User
- **Method**: `PUT`
- **Route**: `/api/v1/app/settings/users/{idOrEmail}`

#### Toggle User Status
- **Method**: `PATCH`
- **Route**: `/api/v1/app/settings/users/{idOrEmail}/status`
- **Request Body**: `{"status": "Inactive"}`

#### Delete User
- **Method**: `DELETE`
- **Route**: `/api/v1/app/settings/users/{idOrEmail}`

---

### 8.5 Tax Master Management (`/api/v1/app/settings/taxes`)

#### Create Tax Master
- **Method**: `POST`
- **Route**: `/api/v1/app/settings/taxes`

#### Request Body
```json
{
  "taxName": "Custom Cess 2%",
  "taxType": "Cess",
  "taxRate": 2.00,
  "description": "Clean energy cess for green initiatives",
  "status": "Active"
}
```

#### Response (201 Created)
```json
{
  "success": true,
  "message": "Tax 'Custom Cess 2%' created successfully.",
  "data": {
    "id": 16,
    "taxName": "Custom Cess 2%",
    "taxRate": 2.00
  }
}
```

---

### 8.6 Follow-Up Defaults (`/api/v1/app/settings/followup`)
- **GET `/api/v1/app/settings/followup`**: Returns system default reminder and escalation settings.
- **PUT `/api/v1/app/settings/followup`**: Updates follow-up defaults.

#### Request Body (PUT)
```json
{
  "defaultReminderDaysBefore": 2,
  "enableEmailReminders": true,
  "enableSmsReminders": true,
  "defaultFollowUpType": "Meeting",
  "autoEscalateAfterDaysOverdue": 4
}
```

#### Response (200 OK)
```json
{
  "success": true,
  "message": "Follow-up settings updated successfully.",
  "data": { ... }
}
```
