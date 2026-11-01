import { Basket } from "./basket";

export interface User {
    email: string;
    token: string;
    roles?: string[];
}

export interface UserSession extends User {
    basket: Basket | null;
}
