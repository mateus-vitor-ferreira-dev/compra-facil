package service

import (
	"context"
	"errors"
	"fmt"
	"strings"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
)

type CategoryService struct {
	categories domain.CategoryRepository
}

func NewCategoryService(categories domain.CategoryRepository) *CategoryService {
	return &CategoryService{categories: categories}
}

func (s *CategoryService) List(ctx context.Context) ([]domain.Category, error) {
	cats, err := s.categories.List(ctx)
	if err != nil {
		return nil, fmt.Errorf("listando categorias: %w", err)
	}
	return cats, nil
}

type CategoryInput struct {
	Slug     string
	Name     string
	ParentID *uuid.UUID
}

func (s *CategoryService) Create(ctx context.Context, in CategoryInput) (domain.Category, error) {
	c := domain.Category{Slug: strings.TrimSpace(in.Slug), Name: strings.TrimSpace(in.Name), ParentID: in.ParentID}
	if err := c.Validate(); err != nil {
		return domain.Category{}, err
	}
	err := s.categories.Create(ctx, &c)
	switch {
	case errors.Is(err, domain.ErrInvalidReference):
		return domain.Category{}, domain.Invalid("parent_id", "categoria pai não existe")
	case errors.Is(err, domain.ErrConflict):
		return domain.Category{}, fmt.Errorf("%w: slug %s já cadastrado", domain.ErrConflict, c.Slug)
	case err != nil:
		return domain.Category{}, fmt.Errorf("gravando categoria: %w", err)
	}
	return c, nil
}
