PRAGMA foreign_keys = ON;

-- SQLite schema reference for ECNESOFT Field Sales.
-- The runtime implementation uses EF Core EnsureCreated with the same structure.

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

CREATE TABLE IF NOT EXISTS RefreshTokens (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER NOT NULL,
    TokenHash TEXT NOT NULL UNIQUE,
    ExpiresAt TEXT NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    RevokedAt TEXT NULL,
    CONSTRAINT FK_RefreshTokens_Users
        FOREIGN KEY (UserId) REFERENCES Users(Id)
        ON UPDATE CASCADE
        ON DELETE CASCADE
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
    ProspectStatus TEXT NULL CHECK (
        ProspectStatus IS NULL OR
        ProspectStatus IN ('ACTIVE', 'OPEN', 'TERMINATION', 'CLOSED', 'PROSPECT', 'OWNERSHIP')
    ),
    Type TEXT NOT NULL CHECK (Type IN ('ACTIVE', 'TERMINATION', 'CLOSED', 'PROSPECT', 'OWNERSHIP')),
    TerminationDate TEXT NULL,
    TerminationReason TEXT NULL,
    Competitor TEXT NULL CHECK (
        Competitor IS NULL OR
        Competitor IN ('KPOS', 'ORDERNOW', 'QONUS', 'SQUARE', 'ETC')
    ),
    GeneralNote TEXT NULL,
    GroupId INTEGER NULL,
    AssignedUserId INTEGER NULL,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
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

CREATE TABLE IF NOT EXISTS HappyVisitGroups (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    GroupName TEXT NOT NULL,
    Type TEXT NOT NULL CHECK (Type IN ('ACTIVE', 'TERMINATION', 'CLOSED', 'PROSPECT', 'OWNERSHIP')),
    CustomerIds TEXT NOT NULL DEFAULT '[]',
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS DashboardPosts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PostName TEXT NOT NULL,
    Editor TEXT NOT NULL,
    Description TEXT NOT NULL,
    ImagePaths TEXT NOT NULL DEFAULT '[]',
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS IX_Users_Email ON Users (Email);
CREATE INDEX IF NOT EXISTS IX_RefreshTokens_TokenHash ON RefreshTokens (TokenHash);
CREATE INDEX IF NOT EXISTS IX_RefreshTokens_User_Expires ON RefreshTokens (UserId, ExpiresAt);
CREATE INDEX IF NOT EXISTS IX_Customers_Type_Postcode_City ON Customers (Type, Postcode, City);
CREATE INDEX IF NOT EXISTS IX_Customers_AssignedUser ON Customers (AssignedUserId);
CREATE INDEX IF NOT EXISTS IX_Customers_Group ON Customers (GroupId);
CREATE INDEX IF NOT EXISTS IX_Customers_Location ON Customers (Latitude, Longitude);
CREATE INDEX IF NOT EXISTS IX_SalesNotes_Customer_VisitedDate ON SalesNotes (CustomerId, VisitedDate DESC);

CREATE VIEW IF NOT EXISTS vw_PenetrationByPostcodeSuburb AS
SELECT
    Postcode,
    City AS Suburb,
    SUM(CASE WHEN Type = 'ACTIVE' THEN 1 ELSE 0 END) AS CurrentCustomers,
    SUM(CASE WHEN Type = 'PROSPECT' THEN 1 ELSE 0 END) AS ProspectStores,
    SUM(CASE WHEN Type IN ('ACTIVE', 'PROSPECT') THEN 1 ELSE 0 END) AS TotalStores,
    ROUND(
        100.0 * SUM(CASE WHEN Type = 'ACTIVE' THEN 1 ELSE 0 END) /
        NULLIF(SUM(CASE WHEN Type IN ('ACTIVE', 'PROSPECT') THEN 1 ELSE 0 END), 0),
        2
    ) AS PenetrationRate,
    AVG(Latitude) AS CentroidLatitude,
    AVG(Longitude) AS CentroidLongitude
FROM Customers
GROUP BY Postcode, City;
