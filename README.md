# InventoryApp (MAUI Proof of Concept)

This is a Proof of Concept (PoC) mobile app I built to dive deeper into cross-platform development with .NET MAUI and C#. The core idea is a straightforward warehouse management tool where employees can scan barcodes, check stock, and update inventory against a cloud-hosted Azure backend. 

## What it actually does

* **Camera Barcode Scanning:** Uses the device camera to read barcodes and fetches the corresponding product details directly from the Azure SQL database.
* **On-the-fly Product Creation:** If an employee scans a barcode that isn't in the system yet, the app prompts them to create and categorize the new item right there on the shop floor.
* **Stock Management:** Simple "+ / -" actions for checking items in and out, including basic validation so stock levels don't drop below zero.
* **Employee Access:** A basic PIN-based login system, including workflows for mandatory PIN changes (e.g., after an admin reset).
* **Local Search:** In-memory filtering allows for quick searches by name or barcode without constantly hitting the API.

## Under the Hood

* **Frontend:** .NET MAUI (iOS & Android) using the MVVM pattern for a clean separation of UI and logic.
* **Backend:** ASP.NET Core Web API, hosted on Microsoft Azure.

## Testing & Stability

To keep the core API endpoints reliable, the backend is covered by an xUnit test suite running against an EF Core In-Memory database. 

Current coverage for the `ProductsController` includes:
* **GET Operations:** Verifying successful product retrieval and making sure the API correctly handles missing records (returning clean 404s).
* **PUT Operations:** Testing the full update cycle (HTTP 204). More importantly, I added tests for edge cases: blocking updates for non-existent IDs (404) and intercepting ID mismatches between the URL route and the JSON payload (HTTP 400) to prevent data manipulation.
* *Technical detail:* To properly simulate isolated, stateless HTTP requests, the tests explicitly clear the EF Core `ChangeTracker` before the act-phase. This prevents the In-Memory DB from falsely passing tests due to cached objects.

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