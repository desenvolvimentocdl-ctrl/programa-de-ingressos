-- Cria o banco do Plateia no MESMO servidor MySQL dos outros sistemas, sem tocar em nenhum outro banco.
-- Rode uma única vez, com um usuário administrador do MySQL.
-- Troque a senha abaixo por uma senha forte antes de executar.

CREATE DATABASE IF NOT EXISTS Plateia
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

-- Usuário próprio do Plateia: só enxerga o banco Plateia.
CREATE USER IF NOT EXISTS 'plateia'@'%' IDENTIFIED BY 'TROQUE_ESTA_SENHA';
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, REFERENCES ON Plateia.* TO 'plateia'@'%';
FLUSH PRIVILEGES;

-- Conferência: deve listar apenas o banco Plateia.
-- SHOW GRANTS FOR 'plateia'@'%';
