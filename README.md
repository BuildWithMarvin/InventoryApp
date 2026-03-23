# Inventory Management App (.NET MAUI)

A cross-platform mobile application built with **.NET MAUI** and **C#** to streamline warehouse and inventory management. This app allows employees to securely log in, scan product barcodes using the device camera, and manage stock levels in real-time via a cloud-based Azure backend.

## Key Features

* **Secure Employee Authentication:** * PIN-based login system for quick access.
    * Enforced PIN-change workflow for new setups or administrative resets.
* **Integrated Barcode Scanning:** * Fast and reliable barcode detection using the device's camera.
    * Directly fetches product details from the Azure database.
* **Real-Time Stock Management:** * Quick **"Stock In (+)"** and **"Stock Out (-)"** actions.
    * Automatic validation prevents stock levels from dropping below zero.
* **Dynamic Product Creation:** * If an unknown barcode is scanned, the app guides the user through a quick step-by-step process to create and categorize the new product directly on the shop floor.
* **High-Performance Search:** * In-memory filtering allows lightning-fast search by name, description, or barcode without network latency.

## Tech Stack & Architecture

<meta name="google-site-verification" content="rHo4BzEMXzBHq12dc6mDhCPwkwq6caDkF_qGHcFAqIg" />

* **Frontend:** .NET MAUI (iOS & Android)
* **Architecture:** MVVM (Model-View-ViewModel) for clean separation of concerns.
* **Backend:** ASP.NET Core Web API (Hosted on Microsoft Azure)
* **Communication:** RESTful API using `HttpClient` and `System.Text.Json`.

## Roadmap & Work in Progress

This project is continuously being improved. The next major focus is the expansion of the **Administrative Features**:

* [ ] **Admin Dashboard:** A dedicated area for warehouse managers to oversee total stock value and recent movements.
* [ ] **Employee Management:** UI to create new employee profiles, assign roles, and force PIN resets remotely.
* [ ] **Pagination for Large Inventories:** Implementing API-side pagination for warehouses with thousands of items to optimize mobile memory usage.
* [ ] **Offline Sync:** Basic offline capabilities for areas with poor warehouse Wi-Fi.

## Getting Started (Local Development)

To run this project locally, you will need Visual Studio 2022 with the **.NET MAUI workload** installed.

1. Clone this repository.
2. Open the solution in Visual Studio.
3. Update the `_apiUrl` in the `ApiService` to point to your local or Azure backend environment.
4. Select your target emulator (Android/iOS) or physical device and hit Run.

---
*Developed by Marvin*
