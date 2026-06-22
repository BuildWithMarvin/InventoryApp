# InventoryApp (MAUI Proof of Concept)

Proof-of-concept inventory management app built with .NET MAUI and ASP.NET Core.employees can scan barcodes, check stock, and update inventory against a cloud hosted Azure backend.

The application allows warehouse employees to scan barcodes, manage stock levels, and synchronize inventory data with an Azure-hosted backend.

## What it actually does

* **Camera Barcode Scanning:** Uses the device camera to read barcodes and fetches the corresponding product details directly from the Azure SQL database.
* **On-the-fly Product Creation:** If an employee scans a barcode that isn't in the system yet, the app prompts them to create and categorize the new item right there on the shop floor.
* **Stock Management:** Simple "+ / -" actions for checking items in and out, including basic validation so stock levels don't drop below zero.
* **Employee Access:** A basic PIN based login system, including workflows for mandatory PIN changes (e.g., after an admin reset).
* **Local Search:** In-memory filtering allows for quick searches by name or barcode without constantly hitting the API.

## Architecture

* **Frontend:** .NET MAUI (iOS & Android) using the MVVM pattern for a clean separation of UI and logic.
* **Backend:** ASP.NET Core Web API, hosted on Microsoft Azure.

## Testing

The backend is covered by an xUnit test suite using EF Core In-Memory testing.
 
Covered scenarios include:

* Product retrieval and 404 handling
* Product updates and validation
* Route/payload ID mismatch detection
* Optimistic concurrency protection (HTTP 409 Conflict)

## Current Focus & Next Steps

Right now, the app works great for basic inventory tracking, but it needs some heavy lifting to scale up for actual warehouse environments. My next goals for the project are:

* **Handling larger datasets:** The mobile app will crash if a warehouse has thousands of items. I need to implement proper API-side pagination next to keep memory usage low on mobile devices.
* **Administrative features:** Building a basic admin UI where managers can manage employee profiles, assign roles, and trigger PIN resets.
* **Dashboard & Analytics:** A simple overview for managers to see total stock value and a live log of recent stock movements.
* **Offline resilience:** Warehouses often have terrible Wi-Fi. I want to look into basic offline caching/syncing so the app doesn't just die when the connection drops.

## Running it locally

You'll need Visual Studio 2022 with the **.NET MAUI workload** installed.

1. Clone this repository and open the solution.
2. Check your `appsettings.json` (or `ApiService`) and update the base URLs to point to your local API instance or your active Azure backend.
3. Select your target emulator (Android/iOS) or plug in a physical device and hit run.

---

*Built by Marvin*