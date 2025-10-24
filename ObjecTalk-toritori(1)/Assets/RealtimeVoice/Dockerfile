# Node.js 22.12を使用したDockerfile
FROM node:22.12-alpine

# 作業ディレクトリを設定
WORKDIR /app

# パッケージファイルをコピー
COPY package*.json ./

# 依存関係をインストール
RUN npm install

# バックエンドのソースコードのみをコピー
COPY backend backend
COPY .env* .

# ポート3001を公開（バックエンドサーバー用）
EXPOSE 3001

# アプリケーションを起動
CMD ["npm", "start"]
