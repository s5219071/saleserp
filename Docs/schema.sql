PRAGMA foreign_keys = ON;

-- SQLite-first schema.
-- SQL Server conversion notes:
--   INTEGER PRIMARY KEY AUTOINCREMENT -> INT IDENTITY(1,1) PRIMARY KEY
--   TEXT -> NVARCHAR(...)
--   REAL -> FLOAT or DECIMAL(9,6)
--   CURRENT_TIMESTAMP -> SYSUTCDATETIME()

CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Email TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    Salt TEXT NOT NULL,
    FullName TEXT NOT NULL,
    Role TEXT NOT NULL CHECK (Role IN ('ADMIN', 'SALES')),
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS ClientGroups (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    GroupName TEXT NOT NULL,
    HexColor TEXT NOT NULL DEFAULT '#00FF00',
    Description TEXT NULL
);

CREATE TABLE IF NOT EXISTS Customers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyName TEXT NOT NULL,
    ABN TEXT NULL,
    Address TEXT NOT NULL,
    City TEXT NOT NULL,
    State TEXT NOT NULL DEFAULT 'NSW',
    Postcode TEXT NOT NULL,
    Phone TEXT NULL,
    Email TEXT NULL,
    Latitude REAL NOT NULL,
    Longitude REAL NOT NULL,
    CustomerType TEXT NOT NULL CHECK (CustomerType IN ('CURRENT', 'PROSPECT')),
    ProspectStatus TEXT NULL CHECK (ProspectStatus IS NULL OR ProspectStatus IN ('ACTIVE', 'OPEN', 'TERMINATION')),
    GroupId INTEGER NULL,
    AssignedUserId INTEGER NULL,
    CONSTRAINT FK_Customers_ClientGroups
        FOREIGN KEY (GroupId) REFERENCES ClientGroups(Id)
        ON UPDATE CASCADE
        ON DELETE SET NULL,
    CONSTRAINT FK_Customers_AssignedUsers
        FOREIGN KEY (AssignedUserId) REFERENCES Users(Id)
        ON UPDATE CASCADE
        ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS SalesNotes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    CompetitorProduct TEXT NULL,
    Notes TEXT NOT NULL,
    ImagePath TEXT NULL,
    VisitedDate TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT FK_SalesNotes_Customers
        FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
        ON UPDATE CASCADE
        ON DELETE CASCADE,
    CONSTRAINT FK_SalesNotes_Users
        FOREIGN KEY (UserId) REFERENCES Users(Id)
        ON UPDATE CASCADE
        ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS IX_Users_Email ON Users (Email);
CREATE INDEX IF NOT EXISTS IX_Customers_Type_Status ON Customers (CustomerType, ProspectStatus);
CREATE INDEX IF NOT EXISTS IX_Customers_Postcode_City_Type ON Customers (Postcode, City, CustomerType);
CREATE INDEX IF NOT EXISTS IX_Customers_AssignedUser ON Customers (AssignedUserId);
CREATE INDEX IF NOT EXISTS IX_Customers_Group ON Customers (GroupId);
CREATE INDEX IF NOT EXISTS IX_Customers_Location ON Customers (Latitude, Longitude);
CREATE INDEX IF NOT EXISTS IX_SalesNotes_Customer_VisitedDate ON SalesNotes (CustomerId, VisitedDate DESC);

-- ADMIN dashboard query: penetration by postcode and suburb.
-- Product share = current customers / (current customers + prospect stores) * 100.
CREATE VIEW IF NOT EXISTS vw_PenetrationByPostcodeSuburb AS
SELECT
    Postcode,
    City AS Suburb,
    SUM(CASE WHEN CustomerType = 'CURRENT' THEN 1 ELSE 0 END) AS CurrentCustomers,
    SUM(CASE WHEN CustomerType = 'PROSPECT' THEN 1 ELSE 0 END) AS ProspectStores,
    COUNT(*) AS TotalStores,
    ROUND(
        100.0 * SUM(CASE WHEN CustomerType = 'CURRENT' THEN 1 ELSE 0 END) / NULLIF(COUNT(*), 0),
        2
    ) AS PenetrationRate,
    AVG(Latitude) AS CentroidLatitude,
    AVG(Longitude) AS CentroidLongitude
FROM Customers
GROUP BY Postcode, City;

SELECT
    Postcode,
    Suburb,
    CurrentCustomers,
    ProspectStores,
    TotalStores,
    PenetrationRate,
    CentroidLatitude,
    CentroidLongitude
FROM vw_PenetrationByPostcodeSuburb
ORDER BY ProspectStores DESC, Postcode ASC;
