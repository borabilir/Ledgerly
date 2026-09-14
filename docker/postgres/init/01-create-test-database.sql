SELECT 'CREATE DATABASE ledgerly_tests'
WHERE NOT EXISTS (
    SELECT FROM pg_database WHERE datname = 'ledgerly_tests'
)\gexec
